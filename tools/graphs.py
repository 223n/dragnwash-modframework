#!/usr/bin/env python3
"""A sketch of graphs (docs/GRAPHS.md), for research before the Graphs library is
written: the operations the libraries register today, a checker for graph files,
and a small interpreter with the same limits the library will have.

    python tools/graphs.py inventory         the operations in src/, by library and kind
    python tools/graphs.py check FILE.json   checks a graph file against the operations in src/
    python tools/graphs.py --test            the checker and the interpreter on the example
                                             graph in docs/GRAPHS.md and on broken graphs

Nothing here runs in the game. The operations are read from the C# sources
(Operations.Register calls), not from a running game, so an operation registered
in an unusual way is missed.
"""
import json
import re
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
DOC = ROOT / "docs" / "GRAPHS.md"

FORMAT = 1

# Limits (docs/GRAPHS.md, "Checking, once, at load" and "Running").
MAX_LOOP = 1000          # repeat, while and each: the largest "max"
MAX_WAIT = 600           # seconds
MAX_STEPS_PER_RUN = 10000
MAX_STATEMENTS = 2000
MAX_DEPTH = 32
MAX_RUNS_PER_GRAPH = 8
FAILURES_BEFORE_OFF = 3
MIN_EVERY = 0.5          # seconds, the timer event

# The events a graph can answer, with the values each hands over. The first seven
# exist in the libraries today; timer.every and key.pressed would be the Graphs
# library's own.
EVENTS = {
    "game.started": {"library": "Core", "values": []},
    "scene.loaded": {"library": "Core", "values": ["scene", "mode"]},
    "scene.unloaded": {"library": "Core", "values": ["scene"]},
    "dialogue.node.started": {"library": "Dialogue", "values": ["node"]},
    "dialogue.line.showing": {"library": "Dialogue", "values": ["line_id", "speaker", "text"]},
    "dialogue.option.showing": {"library": "Dialogue", "values": ["line_id", "text"]},
    "saves.written": {"library": "Flags and saves", "values": ["slot"]},
    "timer.every": {"library": "Graphs (new)", "values": [], "settings": ["seconds"]},
    "key.pressed": {"library": "Graphs (new)", "values": ["key"], "settings": ["key"]},
}

STATEMENTS = ("call", "set", "if", "wait", "repeat", "while", "each", "log", "stop")
BINARY = ("eq", "ne", "lt", "le", "gt", "ge", "add", "sub", "mul", "div", "contains")
LISTED = ("and", "or", "join")
UNARY = ("not", "length")
LOG_LEVELS = ("Info", "Warning", "Error")

LIBRARIES = {
    "DragNWash.ModFramework": "Core",
    "DragNWash.ModFramework.ToolWindow": "Tool window",
    "DragNWash.ModFramework.Assets": "Assets",
    "DragNWash.ModFramework.Dialogue": "Dialogue",
    "DragNWash.ModFramework.Text": "Text",
    "DragNWash.ModFramework.Saves": "Flags and saves",
    "DragNWash.ModFramework.Inspector": "Inspector",
    "DragNWash.ModFramework.Overrides": "Overrides",
    "DragNWash.ModFramework.Bridge": "Bridge",
}


# ---- the operations in the sources --------------------------------------------

REGISTER = re.compile(r'Operations\.Register\(\s*[^,]+,\s*"([a-z0-9_.]+)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*,\s*OperationKind\.(\w+)')
PARAMETER = re.compile(r'Operations\.Parameter\(\s*"([a-z0-9_]+)"\s*,\s*OperationType\.(\w+)\s*,\s*(?:\$?"(?:[^"\\]|\\.)*"|\w+)\s*(?:,\s*(true|false))?\s*((?:,\s*"[^"]*")*)\)')
PARAMETER_VAR = re.compile(r'OperationParameter\s+(\w+)\s*=\s*(Operations\.Parameter\(.*\));\s*$', re.M)


def operations():
    """Every operation registered in src/: name -> {library, kind, page_only, parameters}."""
    found = {}
    for path in sorted(SRC.glob("*/**/*.cs")):
        if "obj" in path.parts or "bin" in path.parts or path.name == "Operations.cs":
            continue
        text = path.read_text(encoding="utf-8-sig")
        library = LIBRARIES.get(path.relative_to(SRC).parts[0], path.relative_to(SRC).parts[0])
        shared = {m.group(1): m.group(2) for m in PARAMETER_VAR.finditer(text)}
        starts = [m for m in REGISTER.finditer(text)]
        for i, m in enumerate(starts):
            end = starts[i + 1].start() if i + 1 < len(starts) else len(text)
            body = text[m.end():end]
            # Up to the next registration: its parameters, and "op.PageOnly = true" after it.
            # Operations.Parameter appears nowhere else in these files.
            params = {}
            for pm in PARAMETER.finditer(body):
                params[pm.group(1)] = parameter(pm)
            # Parameters made once and passed by name (Assets: filter, max).
            for name, made in shared.items():
                if re.search(r"[,(]\s*" + name + r"\s*[,)]", body):
                    pm = PARAMETER.search(made)
                    if pm:
                        params[pm.group(1)] = parameter(pm)
            found[m.group(1)] = {
                "library": library,
                "kind": m.group(3).lower(),
                "page_only": "PageOnly = true" in body,
                "description": m.group(2),
                "parameters": params,
            }
    return found


def parameter(pm):
    choices = re.findall(r'"([^"]*)"', pm.group(4) or "")
    return {"type": pm.group(2).lower(), "required": pm.group(3) == "true", "choices": choices or None}


def inventory():
    ops = operations()
    libraries = {}
    for name, op in ops.items():
        row = libraries.setdefault(op["library"], {"read": 0, "write": 0, "page_only": 0, "names": []})
        row[op["kind"]] += 1
        row["page_only"] += op["page_only"]
        row["names"].append(name)
    print(f"{len(ops)} operations: {sum(o['kind'] == 'read' for o in ops.values())} read, "
          f"{sum(o['kind'] == 'write' for o in ops.values())} write, {sum(o['page_only'] for o in ops.values())} page only; "
          f"{sum(for_graphs(o) for o in ops.values())} a graph could call.")
    for library, row in sorted(libraries.items()):
        print(f"  {library}: {row['read']} read, {row['write']} write, {row['page_only']} page only: {', '.join(sorted(row['names']))}")
    print(f"{len(EVENTS)} events: {', '.join(EVENTS)}")


def for_graphs(op):
    # Page-only operations show the game's own code (content policy); the Bridge's
    # operations open pages and hand out sign-ins. Neither is for graphs.
    return not op["page_only"] and op["library"] != "Bridge"


# ---- checking a graph ------------------------------------------------------------

class Checker:
    def __init__(self, graph, ops):
        self.graph = graph
        self.ops = ops
        self.problems = []
        self.ids = set()
        self.count = 0
        self.uses = {}

    def problem(self, where, text):
        self.problems.append(f"{where}: {text}")

    def run(self):
        g = self.graph
        if not isinstance(g, dict):
            return self.problem("file", "is not a JSON object")
        if not isinstance(g.get("format"), int) or g["format"] < 1:
            self.problem("format", "needs \"format\": 1")
        elif g["format"] > FORMAT:
            self.problem("format", f"is {g['format']}; this reads up to {FORMAT}")
        for key in g:
            if key not in ("format", "name", "description", "variables", "on", "layout"):
                self.problem(key, "is not part of a graph file")
        variables = g.get("variables", {})
        if not isinstance(variables, dict):
            self.problem("variables", "is an object of names and first values")
            variables = {}
        for name, value in variables.items():
            if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,63}", name):
                self.problem(f"variables.{name}", "names are letters, digits and _")
            if isinstance(value, (list, dict)):
                self.problem(f"variables.{name}", "starts as text, a number, true/false or null")
        handlers = g.get("on")
        if not isinstance(handlers, list) or not handlers:
            self.problem("on", "needs at least one event")
            handlers = []
        for h in handlers:
            self.handler(h, set(variables))
        if self.count > MAX_STATEMENTS:
            self.problem("file", f"has {self.count} statements; at most {MAX_STATEMENTS}")
        return self.problems

    def id(self, node, where):
        i = node.get("id")
        if not isinstance(i, str) or not i:
            self.problem(where, "needs an \"id\"")
            return where
        if i in self.ids:
            self.problem(i, "the id is used twice")
        self.ids.add(i)
        return i

    def handler(self, h, graph_vars):
        if not isinstance(h, dict):
            return self.problem("on", "each entry is an object")
        where = self.id(h, "on")
        event = h.get("event")
        if event not in EVENTS:
            return self.problem(where, f"no event named {event!r}; there are {', '.join(EVENTS)}")
        spec = EVENTS[event]
        if event == "timer.every":
            s = h.get("seconds")
            if not isinstance(s, (int, float)) or s < MIN_EVERY:
                self.problem(where, f"timer.every needs \"seconds\" of {MIN_EVERY} or more")
        if event == "key.pressed" and not isinstance(h.get("key"), str):
            self.problem(where, "key.pressed needs \"key\" (a key name such as F6)")
        if h.get("overlap", "skip") not in ("skip", "queue"):
            self.problem(where, "\"overlap\" is skip or queue")
        scope = {"graph": set(graph_vars), "local": set(), "event": set(spec["values"])}
        if "when" in h:
            self.expr(h["when"], where, scope)
        self.block(h.get("do"), where, scope, 1)

    def block(self, stmts, where, scope, depth):
        if not isinstance(stmts, list):
            return self.problem(where, "needs a list of statements")
        if depth > MAX_DEPTH:
            return self.problem(where, f"nested more than {MAX_DEPTH} deep")
        for s in stmts:
            self.statement(s, where, scope, depth)

    def statement(self, s, where, scope, depth):
        if not isinstance(s, dict):
            return self.problem(where, "a statement is an object")
        self.count += 1
        where = self.id(s, where)
        kinds = [k for k in STATEMENTS if k in s]
        if len(kinds) != 1:
            return self.problem(where, f"a statement is exactly one of {', '.join(STATEMENTS)}")
        kind = kinds[0]
        if kind == "call":
            self.call(s, where, scope, depth)
        elif kind == "set":
            if s["set"] not in scope["graph"]:
                self.problem(where, f"set: {s['set']!r} is not in \"variables\"")
            self.expr(s.get("to"), where, scope)
        elif kind == "if":
            self.expr(s["if"], where, scope)
            self.block(s.get("then", []), where, scope, depth + 1)
            self.block(s.get("else", []), where, scope, depth + 1)
        elif kind == "wait":
            w = s["wait"]
            if not isinstance(w, (int, float)) or isinstance(w, bool) or not 0 <= w <= MAX_WAIT:
                self.problem(where, f"wait is a number of seconds, 0 (the next frame) to {MAX_WAIT}")
        elif kind in ("repeat", "while", "each"):
            if kind == "repeat":
                n = s["repeat"]
                if not isinstance(n, int) or isinstance(n, bool) or not 1 <= n <= MAX_LOOP:
                    self.problem(where, f"repeat is a whole number, 1 to {MAX_LOOP}")
            else:
                m = s.get("max")
                if not isinstance(m, int) or isinstance(m, bool) or not 1 <= m <= MAX_LOOP:
                    self.problem(where, f"{kind} needs \"max\", 1 to {MAX_LOOP}")
                self.expr(s[kind], where, scope)
            if kind == "each":
                self.local(s.get("as"), where, scope)
            self.block(s.get("do"), where, scope, depth + 1)
        elif kind == "log":
            self.expr(s["log"], where, scope)
            if s.get("level", "Info") not in LOG_LEVELS:
                self.problem(where, f"level is one of {', '.join(LOG_LEVELS)}")
        elif kind == "stop":
            if s["stop"] is not True:
                self.problem(where, "stop is written \"stop\": true")

    def call(self, s, where, scope, depth):
        name = s["call"]
        if not isinstance(name, str):
            return self.problem(where, "the operation's name is written out, not computed")
        op = self.ops.get(name)
        if op is None:
            return self.problem(where, f"no operation named {name}")
        if not for_graphs(op):
            return self.problem(where, f"{name} is not for graphs")
        self.uses[name] = op["kind"]
        args = s.get("args", {})
        if not isinstance(args, dict):
            return self.problem(where, "args is an object")
        for pname, p in op["parameters"].items():
            if p["required"] and pname not in args:
                self.problem(where, f"{name} needs {pname}")
        for aname, value in args.items():
            p = op["parameters"].get(aname)
            if p is None:
                self.problem(where, f"{name} has no parameter {aname}; it takes {', '.join(op['parameters']) or 'none'}")
                continue
            self.expr(value, where, scope)
            if not isinstance(value, dict):
                literal_ok = {"string": lambda v: isinstance(v, str), "number": lambda v: isinstance(v, (int, float)) and not isinstance(v, bool),
                              "boolean": lambda v: isinstance(v, bool)}[p["type"]]
                if not literal_ok(value):
                    self.problem(where, f"{name}: {aname} is a {p['type']}")
                elif p["choices"] and value not in p["choices"]:
                    self.problem(where, f"{name}: {aname} is one of {', '.join(p['choices'])}")
        if "as" in s:
            self.local(s["as"], where, scope)
        if "onError" in s:
            scope["local"].add("error")
            self.block(s["onError"], where, scope, depth + 1)

    def local(self, name, where, scope):
        if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,63}", name):
            return self.problem(where, "\"as\" names a variable: letters, digits and _")
        if name in scope["graph"]:
            self.problem(where, f"{name} is a graph variable; a result needs a name of its own")
        scope["local"].add(name)

    def expr(self, e, where, scope):
        if e is None or isinstance(e, (str, int, float, bool)):
            return
        if isinstance(e, list):
            return self.problem(where, "a list is written with an operator (join, and, or)")
        if not isinstance(e, dict) or len(e) != 1:
            return self.problem(where, "an expression is a value or an object with one operator")
        (op, arg), = e.items()
        if op == "var":
            if arg not in scope["graph"] and arg not in scope["local"]:
                self.problem(where, f"no variable {arg!r} (graph variables, or a result named with \"as\" earlier)")
        elif op == "event":
            if arg not in scope["event"]:
                self.problem(where, f"this event has no value {arg!r}; it has {', '.join(sorted(scope['event'])) or 'none'}")
        elif op == "get":
            if not isinstance(arg, list) or len(arg) < 2:
                return self.problem(where, "get is [value, key or index, ...]")
            self.expr(arg[0], where, scope)
            for k in arg[1:]:
                if not isinstance(k, (str, int)) or isinstance(k, bool):
                    self.problem(where, "get's keys are text or whole numbers")
        elif op in BINARY:
            if not isinstance(arg, list) or len(arg) != 2:
                return self.problem(where, f"{op} takes two values")
            for a in arg:
                self.expr(a, where, scope)
        elif op in LISTED:
            if not isinstance(arg, list) or not arg:
                return self.problem(where, f"{op} takes a list")
            for a in arg:
                self.expr(a, where, scope)
        elif op in UNARY:
            self.expr(arg, where, scope)
        else:
            self.problem(where, f"no operator {op!r}")


def check(graph, ops):
    c = Checker(graph, ops)
    problems = c.run()
    return problems, c.uses


# ---- running a graph ----------------------------------------------------------------

class Failed(Exception):
    pass


class Run:
    """One run of one handler: a stack of (statements, position, loop state)."""

    def __init__(self, handler, values):
        self.handler = handler
        self.event = values
        self.locals = {}
        self.stack = [[handler["do"], 0, None]]
        self.steps = 0
        self.wake = 0.0
        self.done = False


class Interpreter:
    """Runs a checked graph. call(name, args) is the registry; clock() the time."""

    def __init__(self, graph, call, clock, budget_steps=200, log=None):
        self.graph = graph
        self.call = call
        self.clock = clock
        self.budget_steps = budget_steps
        self.vars = dict(graph.get("variables", {}))
        self.runs = []
        self.failures = 0
        self.off = False
        self.log = log if log is not None else []

    def raise_event(self, event, values=None):
        if self.off:
            return
        for h in self.graph["on"]:
            if h["event"] != event:
                continue
            alive = [r for r in self.runs if r.handler is h and not r.done]
            if alive and h.get("overlap", "skip") == "skip":
                continue
            if sum(not r.done for r in self.runs) >= MAX_RUNS_PER_GRAPH:
                continue
            run = Run(h, dict(values or {}))
            if "when" in h and not truthy(self.value(h["when"], run)):
                continue
            self.runs.append(run)

    def frame(self):
        """One frame: runs statements until the step budget is used. Returns the steps taken."""
        taken = 0
        for run in list(self.runs):
            if self.off:
                break
            while not run.done and run.wake <= self.clock() and taken < self.budget_steps:
                try:
                    self.step(run)
                except Failed as ex:
                    self.fail(run, str(ex))
                taken += 1
            if run.done and run in self.runs:
                self.runs.remove(run)
        return taken

    def fail(self, run, why):
        run.done = True
        self.failures += 1
        self.log.append(("Error", f"{run.handler['id']}: {why}"))
        if self.failures >= FAILURES_BEFORE_OFF:
            self.off = True
            self.runs.clear()
            self.log.append(("Error", f"failed {self.failures} times in a row and is switched off for this session"))

    def step(self, run):
        run.steps += 1
        if run.steps > MAX_STEPS_PER_RUN:
            raise Failed(f"more than {MAX_STEPS_PER_RUN} steps in one run")
        frame = run.stack[-1]
        stmts, pos, loop = frame
        if pos >= len(stmts):
            if not self.loop_again(run, frame):
                run.stack.pop()
                if not run.stack:
                    run.done = True
                    self.failures = 0
            return
        s = stmts[pos]
        frame[1] += 1
        if "call" in s:
            args = {k: self.value(v, run) for k, v in s.get("args", {}).items()}
            ok, result = self.call(s["call"], args)
            if ok:
                if "as" in s:
                    run.locals[s["as"]] = result
            elif "onError" in s:
                run.locals["error"] = result
                run.stack.append([s["onError"], 0, None])
            else:
                raise Failed(f"{s['id']}: {s['call']}: {result}")
        elif "set" in s:
            self.vars[s["set"]] = self.value(s["to"], run)
        elif "if" in s:
            branch = s.get("then", []) if truthy(self.value(s["if"], run)) else s.get("else", [])
            run.stack.append([branch, 0, None])
        elif "wait" in s:
            run.wake = self.clock() + s["wait"]
            if s["wait"] == 0:
                run.wake = self.clock() + 1e-9
        elif "repeat" in s:
            run.stack.append([s["do"], 0, {"left": s["repeat"] - 1}])
        elif "while" in s:
            if truthy(self.value(s["while"], run)):
                run.stack.append([s["do"], 0, {"while": s, "left": s["max"] - 1}])
        elif "each" in s:
            items = self.value(s["each"], run)
            if not isinstance(items, list):
                raise Failed(f"{s['id']}: each needs a list")
            if len(items) > s["max"]:
                raise Failed(f"{s['id']}: {len(items)} items, more than max {s['max']}")
            if items:
                run.locals[s["as"]] = items[0]
                run.stack.append([s["do"], 0, {"items": items, "at": 0, "as": s["as"]}])
        elif "log" in s:
            self.log.append((s.get("level", "Info"), text(self.value(s["log"], run))))
        elif "stop" in s:
            run.stack.clear()
            run.done = True
            self.failures = 0

    def loop_again(self, run, frame):
        loop = frame[2]
        if loop is None:
            return False
        if "items" in loop:
            loop["at"] += 1
            if loop["at"] >= len(loop["items"]):
                return False
            run.locals[loop["as"]] = loop["items"][loop["at"]]
        elif "while" in loop:
            if not truthy(self.value(loop["while"]["while"], run)):
                return False
            if loop["left"] <= 0:
                raise Failed(f"{loop['while']['id']}: still true after max {loop['while']['max']} rounds")
            loop["left"] -= 1
        else:
            if loop["left"] <= 0:
                return False
            loop["left"] -= 1
        frame[1] = 0
        return True

    def value(self, e, run):
        if not isinstance(e, dict):
            return e
        (op, arg), = e.items()
        if op == "var":
            return run.locals[arg] if arg in run.locals else self.vars.get(arg)
        if op == "event":
            return run.event.get(arg)
        if op == "get":
            v = self.value(arg[0], run)
            for k in arg[1:]:
                if isinstance(v, dict):
                    v = v.get(k)
                elif isinstance(v, list) and isinstance(k, int) and -len(v) <= k < len(v):
                    v = v[k]
                else:
                    return None
            return v
        if op in LISTED:
            vals = [self.value(a, run) for a in arg]
            if op == "join":
                return "".join(text(v) for v in vals)
            return all(truthy(v) for v in vals) if op == "and" else any(truthy(v) for v in vals)
        if op == "not":
            return not truthy(self.value(arg, run))
        if op == "length":
            v = self.value(arg, run)
            return len(v) if isinstance(v, (list, str, dict)) else 0
        a, b = (self.value(x, run) for x in arg)
        if op == "contains":
            if isinstance(a, list):
                return b in a
            return text(b).lower() in text(a).lower()
        if op in ("eq", "ne"):
            same = a == b if type(a) == type(b) or (number(a) and number(b)) else text(a) == text(b)
            return same if op == "eq" else not same
        if op in ("lt", "le", "gt", "ge"):
            if not (number(a) and number(b)):
                a, b = text(a), text(b)
            return {"lt": a < b, "le": a <= b, "gt": a > b, "ge": a >= b}[op]
        if not (number(a) and number(b)):
            raise Failed(f"{op} needs numbers, got {text(a)!r} and {text(b)!r}")
        if op == "div" and b == 0:
            raise Failed("division by zero")
        return {"add": a + b, "sub": a - b, "mul": a * b, "div": a / b if op == "div" else 0}[op]


def number(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def truthy(v):
    return v not in (None, False, 0, "", [], {})


def text(v):
    if v is None:
        return ""
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, float) and v.is_integer():
        return str(int(v))
    if isinstance(v, (list, dict)):
        return json.dumps(v, ensure_ascii=False)
    return str(v)


# ---- tests ---------------------------------------------------------------------

def example_graph():
    """The example in docs/GRAPHS.md: the first ```json block after <!-- example-graph -->."""
    doc = DOC.read_text(encoding="utf-8")
    at = doc.index("<!-- example-graph -->")
    block = re.search(r"```json\n(.*?)```", doc[at:], re.S)
    return json.loads(block.group(1))


def test():
    ops = operations()
    failures = []

    def expect(what, ok):
        if not ok:
            failures.append(what)

    expect("the registry has read operations", sum(o["kind"] == "read" for o in ops.values()) >= 20)
    expect("inspector.member.get has its parameters", set(ops["inspector.member.get"]["parameters"]) == {"path", "component", "index", "member", "private"})
    expect("assets.textures.list has filter and max", set(ops["assets.textures.list"]["parameters"]) == {"filter", "max"})
    expect("log.read's level has choices", ops["log.read"]["parameters"]["level"]["choices"] is not None)
    expect("code.graph is page only", ops["code.graph"]["page_only"])

    graph = example_graph()
    problems, uses = check(graph, ops)
    expect(f"the example graph checks clean: {problems}", not problems)
    expect("the example uses only reads", set(uses.values()) == {"read"})

    broken = [
        ({"format": 1, "on": [{"id": "h", "event": "scene.loaded", "do": [{"id": "a", "call": "code.graph", "args": {"method": "x"}}]}]}, "not for graphs"),
        ({"format": 1, "on": [{"id": "h", "event": "scene.loaded", "do": [{"id": "a", "call": "saves.flags.get", "args": {"slot": "1"}}]}]}, "needs id"),
        ({"format": 1, "on": [{"id": "h", "event": "scene.loaded", "do": [{"id": "a", "while": True, "do": []}]}]}, "needs \"max\""),
        ({"format": 1, "on": [{"id": "h", "event": "scene.loaded", "do": [{"id": "a", "log": {"var": "nope"}}]}]}, "no variable"),
        ({"format": 1, "on": [{"id": "h", "event": "scene.loaded", "do": [{"id": "a", "log": {"event": "slot"}}]}]}, "no value 'slot'"),
        ({"format": 1, "on": [{"id": "h", "event": "scene.loaded", "do": [{"id": "a", "call": "log.read", "args": {"level": "Loud"}}]}]}, "one of"),
        ({"format": 2, "on": [{"id": "h", "event": "scene.loaded", "do": []}]}, "reads up to"),
        ({"format": 1, "on": [{"id": "h", "event": "key.pressed", "do": [{"id": "h", "stop": True}]}]}, "used twice"),
    ]
    for g, words in broken:
        p, _ = check(g, ops)
        expect(f"broken graph says {words!r}: {p}", any(words in x for x in p))

    # The interpreter, with fake operations standing in for the game.
    now = [0.0]
    roots = [{"path": "Level", "scene": "Main", "active": True, "children": 120}, {"path": "UI", "scene": "Main", "active": True, "children": 8}]
    fake = {
        "inspector.objects.children": lambda a: (True, roots),
        "saves.flags.list": lambda a: (True, [{"id": "met_kobold", "value": True}] * 3),
        "dialogue.current": lambda a: (True, {"node": "Intro"}),
    }
    calls = []

    def call(name, args):
        calls.append(name)
        return fake[name](args) if name in fake else (False, "no such operation")

    it = Interpreter(graph, call, lambda: now[0])
    it.raise_event("scene.loaded", {"scene": "Main", "mode": "Single"})
    it.raise_event("scene.loaded", {"scene": "Main", "mode": "Single"})  # skipped: a run is alive
    for _ in range(5):
        it.frame()
    expect("the run waits a second", not any("root objects" in m for _, m in it.log))
    now[0] = 1.5
    for _ in range(5):
        it.frame()
    lines = [m for _, m in it.log]
    expect(f"scene line logged once: {lines}", sum("root objects" in m for m in lines) == 1)
    expect(f"the big root is warned about: {lines}", any(lvl == "Warning" and "Level" in m for lvl, m in it.log))
    it.raise_event("saves.written", {"slot": "slot1"})
    it.frame()
    expect(f"the save line: {it.log[-1]}", it.log[-1][1].startswith("slot1 was saved: 3 flags"))

    # Three failures in a row switch a graph off.
    bad = {"format": 1, "on": [{"id": "h", "event": "game.started", "do": [{"id": "a", "call": "missing.op"}]}]}
    it = Interpreter(bad, call, lambda: now[0])
    for _ in range(4):
        it.raise_event("game.started")
        it.frame()
    expect("switched off after three failures", it.off and it.failures == 3)

    # A while loop that never ends fails at its max instead of hanging.
    spin = {"format": 1, "variables": {"n": 0}, "on": [{"id": "h", "event": "game.started", "do": [
        {"id": "w", "while": True, "max": 50, "do": [{"id": "s", "set": "n", "to": {"add": [{"var": "n"}, 1]}}]}]}]}
    it = Interpreter(spin, call, lambda: now[0])
    it.raise_event("game.started")
    for _ in range(10):
        it.frame()
    expect(f"the endless while stops at max: {it.log}", it.vars["n"] == 50 and it.failures == 1)

    # How much of a frame the interpreter itself takes (Python; the C# numbers are in the doc).
    count = {"format": 1, "variables": {"n": 0}, "on": [{"id": "h", "event": "game.started", "do": [
        {"id": "r", "repeat": 1000, "do": [{"id": "s", "set": "n", "to": {"add": [{"var": "n"}, 1]}},
                                            {"id": "i", "if": {"gt": [{"var": "n"}, 5]}, "then": []}]}]}]}
    it = Interpreter(count, call, lambda: now[0], budget_steps=10 ** 9)
    it.raise_event("game.started")
    t = time.perf_counter()
    steps = it.frame()
    ms = (time.perf_counter() - t) * 1000
    expect("the counting graph counts", it.vars["n"] == 1000)

    for f in failures:
        print("FAIL:", f)
    if failures:
        sys.exit(1)
    print(f"OK: {len(ops)} operations read from src/, the example graph checks and runs, "
          f"{len(broken)} broken graphs refused; {steps} interpreter steps took {ms:.2f} ms here (Python).")


def main():
    args = sys.argv[1:]
    if args == ["--test"]:
        return test()
    if args == ["inventory"]:
        return inventory()
    if len(args) == 2 and args[0] == "check":
        graph = json.loads(Path(args[1]).read_text(encoding="utf-8-sig"))
        problems, uses = check(graph, operations())
        for p in problems:
            print(p)
        print(f"{'OK' if not problems else str(len(problems)) + ' problem(s)'}; uses: "
              + (", ".join(f"{n} ({k})" for n, k in sorted(uses.items())) or "no operations"))
        return sys.exit(1 if problems else 0)
    print(__doc__)
    sys.exit(2)


if __name__ == "__main__":
    main()
