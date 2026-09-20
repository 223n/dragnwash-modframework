using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DragNWash.ModFramework.Graphs
{
    // Reads one graph file and checks it completely, against the operations and
    // events registered now: the shape, the limits, every call and every name.
    // A file with one problem does not run at all, so the problems are all
    // collected instead of stopping at the first, and the statements are built
    // as they are checked: the tree is kept only when nothing is wrong.
    //
    // The same rules are in tools/graphs.py, which checks them without the game;
    // the messages here say the same things in the same words.
    internal sealed class GraphCheck
    {
        private static readonly Regex Name = new Regex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant);

        private static readonly string[] FileKeys = { "format", "name", "description", "variables", "on", "layout" };
        private static readonly string[] StatementKeys = { "call", "set", "if", "wait", "repeat", "while", "each", "log", "stop" };

        private readonly IGraphWorld _world;
        private readonly Graph _graph;
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);
        private int _count;

        private GraphCheck(IGraphWorld world, Graph graph)
        {
            _world = world;
            _graph = graph;
        }

        /// <summary>
        /// Reads a graph's text into <paramref name="graph"/>: its handlers when
        /// it is sound, its problems when it is not.
        /// </summary>
        internal static void Read(IGraphWorld world, Graph graph, string text)
        {
            // The byte length is checked by whoever opens the file; this catches
            // text that came from somewhere else (the editor's page, a test).
            if (text != null && text.Length > GraphLimits.MaxFileBytes)
            {
                graph.Problems.Add($"file: is {text.Length / 1024} KB; at most {GraphLimits.MaxFileBytes / 1024} KB");
                return;
            }
            object json;
            try
            {
                json = Json.Parse(text);
            }
            catch (Exception ex)
            {
                graph.Problems.Add("file: is not JSON: " + ex.Message);
                return;
            }
            new GraphCheck(world, graph).File(json);
            if (!graph.Ok)
            {
                // Half a tree runs nothing; the problems are what is left.
                graph.Handlers.Clear();
            }
            graph.Variables = new Dictionary<string, object>(graph.FirstValues, StringComparer.Ordinal);
        }

        private void Problem(string where, string text) => _graph.Problems.Add(where + ": " + text);

        private void File(object json)
        {
            if (!(json is Dictionary<string, object> g))
            {
                Problem("file", "is not a JSON object");
                return;
            }
            object format;
            if (!g.TryGetValue("format", out format) || !GraphValues.IsNumber(format) || GraphValues.Number(format) < 1)
            {
                Problem("format", "needs \"format\": 1");
            }
            else if (GraphValues.Number(format) > GraphLimits.Format)
            {
                Problem("format", $"is {GraphValues.Text(format)}; this reads up to {GraphLimits.Format}. Update the framework.");
            }
            foreach (string key in g.Keys)
            {
                // A typo is found here rather than quietly ignored.
                if (Array.IndexOf(FileKeys, key) < 0)
                {
                    Problem(key, "is not part of a graph file");
                }
            }
            _graph.Name = Json.String(g, "name") ?? _graph.File;
            _graph.Description = Json.String(g, "description");

            object variables;
            if (g.TryGetValue("variables", out variables) && variables != null)
            {
                if (variables is Dictionary<string, object> vars)
                {
                    foreach (KeyValuePair<string, object> kv in vars)
                    {
                        if (!Name.IsMatch(kv.Key))
                        {
                            Problem("variables." + kv.Key, "names are letters, digits and _");
                        }
                        if (kv.Value is List<object> || kv.Value is Dictionary<string, object>)
                        {
                            Problem("variables." + kv.Key, "starts as text, a number, true/false or null");
                            continue;
                        }
                        _graph.FirstValues[kv.Key] = kv.Value;
                    }
                }
                else
                {
                    Problem("variables", "is an object of names and first values");
                }
            }

            object on;
            if (!g.TryGetValue("on", out on) || !(on is List<object> handlers) || handlers.Count == 0)
            {
                Problem("on", "needs at least one event");
                return;
            }
            foreach (object h in handlers)
            {
                Handler(h);
            }
            if (_count > GraphLimits.MaxStatements)
            {
                Problem("file", $"has {_count} statements; at most {GraphLimits.MaxStatements}");
            }
        }

        // Ids point at a block in the editor, in the log and on the Mods screen,
        // so each must be there and each must be its own.
        private string Id(Dictionary<string, object> node, string where)
        {
            string id = Json.String(node, "id");
            if (string.IsNullOrEmpty(id))
            {
                Problem(where, "needs an \"id\"");
                return where;
            }
            if (!_ids.Add(id))
            {
                Problem(id, "the id is used twice");
            }
            return id;
        }

        private void Handler(object raw)
        {
            if (!(raw is Dictionary<string, object> h))
            {
                Problem("on", "each entry is an object");
                return;
            }
            string where = Id(h, "on");
            string name = Json.String(h, "event");
            GraphEventKind kind = name != null ? _world.FindEvent(name) : null;
            if (kind == null)
            {
                string library = Missing(name);
                Problem(where, library != null
                    ? $"the event {name} needs the {library} library, which is not installed"
                    : $"no event named \"{name}\"; there are {Listed(_world.EventNames)}");
                return;
            }

            var handler = new GraphHandler { Id = where, Event = name };
            if (name == GraphEvents.Timer)
            {
                object seconds;
                if (!h.TryGetValue("seconds", out seconds) || !GraphValues.IsNumber(seconds) || GraphValues.Number(seconds) < GraphLimits.MinEvery)
                {
                    Problem(where, $"{GraphEvents.Timer} needs \"seconds\" of {GraphLimits.MinEvery.ToString(CultureInfo.InvariantCulture)} or more");
                }
                else
                {
                    handler.Seconds = GraphValues.Number(seconds);
                }
            }
            if (name == GraphEvents.Key)
            {
                string key = Json.String(h, "key");
                if (string.IsNullOrEmpty(key))
                {
                    Problem(where, $"{GraphEvents.Key} needs \"key\" (a key name such as F6)");
                }
                else
                {
                    string why = _world.KeyProblem(key);
                    if (why != null)
                    {
                        Problem(where, why);
                    }
                    handler.Key = key;
                }
            }

            string overlap = Json.String(h, "overlap") ?? "skip";
            if (overlap != "skip" && overlap != "queue")
            {
                Problem(where, "\"overlap\" is skip or queue");
            }
            handler.Queue = overlap == "queue";

            var scope = new Scope(_graph.FirstValues.Keys, kind.Values);
            object when;
            if (h.TryGetValue("when", out when))
            {
                handler.When = Expression(when, where, scope);
            }
            object todo;
            h.TryGetValue("do", out todo);
            handler.Do = Block(todo, where, scope, 1);
            _graph.Handlers.Add(handler);
        }

        private List<GraphStmt> Block(object raw, string where, Scope scope, int depth)
        {
            var into = new List<GraphStmt>();
            if (!(raw is List<object> statements))
            {
                Problem(where, "needs a list of statements");
                return into;
            }
            if (depth > GraphLimits.MaxDepth)
            {
                Problem(where, $"nested more than {GraphLimits.MaxDepth} deep");
                return into;
            }
            foreach (object s in statements)
            {
                GraphStmt built = Statement(s, where, scope, depth);
                if (built != null)
                {
                    into.Add(built);
                }
            }
            return into;
        }

        private GraphStmt Statement(object raw, string where, Scope scope, int depth)
        {
            if (!(raw is Dictionary<string, object> s))
            {
                Problem(where, "a statement is an object");
                return null;
            }
            _count++;
            where = Id(s, where);
            string[] kinds = StatementKeys.Where(s.ContainsKey).ToArray();
            if (kinds.Length != 1)
            {
                Problem(where, "a statement is exactly one of " + string.Join(", ", StatementKeys));
                return null;
            }
            switch (kinds[0])
            {
                case "call": return Call(s, where, scope, depth);
                case "set": return Set(s, where, scope);
                case "if": return If(s, where, scope, depth);
                case "wait": return Wait(s, where);
                case "repeat": return Repeat(s, where, scope, depth);
                case "while": return While(s, where, scope, depth);
                case "each": return Each(s, where, scope, depth);
                case "log": return Log(s, where, scope);
                default: return Stop(s, where);
            }
        }

        private GraphStmt Call(Dictionary<string, object> s, string where, Scope scope, int depth)
        {
            var call = new CallStmt { Id = where };
            if (!(s["call"] is string name))
            {
                Problem(where, "the operation's name is written out, not computed");
                return null;
            }
            call.Operation = name;
            GraphOperation op = _world.FindOperation(name);
            if (op == null)
            {
                // The operation may be perfectly good and its library simply not
                // installed; saying which one is more use than "no such name".
                string library = Missing(name);
                Problem(where, library != null
                    ? $"{name} needs the {library} library, which is not installed"
                    : $"no operation named {name}");
                return null;
            }
            if (!op.ForGraphs || op.Lasting)
            {
                Problem(where, op.Lasting
                    ? $"{name} changes what outlives the session, which graphs may not do"
                    : $"{name} is not for graphs");
                return null;
            }
            _graph.Uses[name] = op.Changes;

            object raw;
            s.TryGetValue("args", out raw);
            var args = raw as Dictionary<string, object>;
            if (raw != null && args == null)
            {
                Problem(where, "args is an object");
                return null;
            }
            foreach (GraphParameter p in op.Parameters)
            {
                if (p.Required && (args == null || !args.ContainsKey(p.Name)))
                {
                    Problem(where, $"{name} needs {p.Name}");
                }
            }
            if (args != null)
            {
                foreach (KeyValuePair<string, object> kv in args)
                {
                    GraphParameter p = op.Parameters.FirstOrDefault(x => x.Name == kv.Key);
                    if (p == null)
                    {
                        Problem(where, $"{name} has no parameter {kv.Key}; it takes {Listed(op.Parameters.Select(x => x.Name).ToArray())}");
                        continue;
                    }
                    GraphExpr value = Expression(kv.Value, where, scope);
                    // A value written out is checked now; one that is worked out
                    // while the graph runs is checked by the registry then.
                    if (!(kv.Value is Dictionary<string, object>))
                    {
                        Literal(kv.Value, p, name, where);
                    }
                    if (value != null)
                    {
                        call.Args.Add(new KeyValuePair<string, GraphExpr>(kv.Key, value));
                    }
                }
            }
            if (s.ContainsKey("as"))
            {
                call.As = Local(s["as"], where, scope);
            }
            object onError;
            if (s.TryGetValue("onError", out onError))
            {
                scope.Local.Add("error");
                call.OnError = Block(onError, where, scope, depth + 1);
            }
            return call;
        }

        private void Literal(object value, GraphParameter p, string name, string where)
        {
            bool ok;
            switch (p.Type)
            {
                case GraphValueType.Number: ok = GraphValues.IsNumber(value); break;
                case GraphValueType.Boolean: ok = value is bool; break;
                default: ok = value is string; break;
            }
            if (!ok)
            {
                Problem(where, $"{name}: {p.Name} is {Article(p.Type)}");
                return;
            }
            if (p.Choices != null && p.Choices.Length > 0 && !p.Choices.Contains((string)value, StringComparer.OrdinalIgnoreCase))
            {
                Problem(where, $"{name}: {p.Name} is one of {string.Join(", ", p.Choices)}");
            }
        }

        private static string Article(GraphValueType type)
        {
            switch (type)
            {
                case GraphValueType.Number: return "a number";
                case GraphValueType.Boolean: return "true or false";
                default: return "text";
            }
        }

        private GraphStmt Set(Dictionary<string, object> s, string where, Scope scope)
        {
            string name = s["set"] as string;
            if (name == null || !scope.Graph.Contains(name))
            {
                Problem(where, $"set: \"{name}\" is not in \"variables\"");
                return null;
            }
            object to;
            s.TryGetValue("to", out to);
            return new SetStmt { Id = where, Name = name, To = Expression(to, where, scope) ?? GraphExpr.Value(null) };
        }

        private GraphStmt If(Dictionary<string, object> s, string where, Scope scope, int depth)
        {
            object then, otherwise;
            s.TryGetValue("then", out then);
            s.TryGetValue("else", out otherwise);
            return new IfStmt
            {
                Id = where,
                Test = Expression(s["if"], where, scope) ?? GraphExpr.Value(null),
                Then = then == null ? new List<GraphStmt>() : Block(then, where, scope, depth + 1),
                Else = otherwise == null ? new List<GraphStmt>() : Block(otherwise, where, scope, depth + 1),
            };
        }

        private GraphStmt Wait(Dictionary<string, object> s, string where)
        {
            object w = s["wait"];
            if (!GraphValues.IsNumber(w) || GraphValues.Number(w) < 0 || GraphValues.Number(w) > GraphLimits.MaxWait)
            {
                Problem(where, $"wait is a number of seconds, 0 (the next frame) to {GraphLimits.MaxWait}");
                return null;
            }
            return new WaitStmt { Id = where, Seconds = GraphValues.Number(w) };
        }

        private GraphStmt Repeat(Dictionary<string, object> s, string where, Scope scope, int depth)
        {
            int times = Whole(s["repeat"]);
            if (times < 1 || times > GraphLimits.MaxLoop)
            {
                Problem(where, $"repeat is a whole number, 1 to {GraphLimits.MaxLoop}");
                return null;
            }
            object todo;
            s.TryGetValue("do", out todo);
            return new RepeatStmt { Id = where, Times = times, Do = Block(todo, where, scope, depth + 1) };
        }

        private GraphStmt While(Dictionary<string, object> s, string where, Scope scope, int depth)
        {
            // A loop that never ends is a mistake, so "max" is required and the
            // run fails when the test is still true after it.
            int max = Max(s, "while", where);
            GraphExpr test = Expression(s["while"], where, scope);
            object todo;
            s.TryGetValue("do", out todo);
            List<GraphStmt> body = Block(todo, where, scope, depth + 1);
            return max < 1 ? null : new WhileStmt { Id = where, Test = test ?? GraphExpr.Value(null), Max = max, Do = body };
        }

        private GraphStmt Each(Dictionary<string, object> s, string where, Scope scope, int depth)
        {
            int max = Max(s, "each", where);
            GraphExpr items = Expression(s["each"], where, scope);
            string as_ = s.ContainsKey("as") ? Local(s["as"], where, scope) : null;
            object todo;
            s.TryGetValue("do", out todo);
            List<GraphStmt> body = Block(todo, where, scope, depth + 1);
            if (as_ == null)
            {
                if (!s.ContainsKey("as"))
                {
                    Problem(where, "each needs \"as\", a name for the item");
                }
                return null;
            }
            return max < 1 ? null : new EachStmt { Id = where, Items = items ?? GraphExpr.Value(null), As = as_, Max = max, Do = body };
        }

        private int Max(Dictionary<string, object> s, string kind, string where)
        {
            object raw;
            s.TryGetValue("max", out raw);
            int max = Whole(raw);
            if (max < 1 || max > GraphLimits.MaxLoop)
            {
                Problem(where, $"{kind} needs \"max\", 1 to {GraphLimits.MaxLoop}");
                return 0;
            }
            return max;
        }

        private GraphStmt Log(Dictionary<string, object> s, string where, Scope scope)
        {
            var log = new LogStmt { Id = where, Text = Expression(s["log"], where, scope) ?? GraphExpr.Value(null) };
            string level = Json.String(s, "level") ?? "Info";
            switch (level)
            {
                case "Info": log.Level = GraphLevel.Info; break;
                case "Warning": log.Level = GraphLevel.Warning; break;
                case "Error": log.Level = GraphLevel.Error; break;
                default:
                    Problem(where, "level is one of Info, Warning, Error");
                    return null;
            }
            return log;
        }

        private GraphStmt Stop(Dictionary<string, object> s, string where)
        {
            if (!(s["stop"] is bool on) || !on)
            {
                Problem(where, "stop is written \"stop\": true");
                return null;
            }
            return new StopStmt { Id = where };
        }

        private string Local(object raw, string where, Scope scope)
        {
            string name = raw as string;
            if (name == null || !Name.IsMatch(name))
            {
                Problem(where, "\"as\" names a variable: letters, digits and _");
                return null;
            }
            if (scope.Graph.Contains(name))
            {
                Problem(where, $"{name} is a graph variable; a result needs a name of its own");
                return null;
            }
            scope.Local.Add(name);
            return name;
        }

        private GraphExpr Expression(object e, string where, Scope scope)
        {
            if (e == null || e is string || e is bool || GraphValues.IsNumber(e))
            {
                return GraphExpr.Value(e);
            }
            if (e is List<object>)
            {
                Problem(where, "a list is written with an operator (join, and, or)");
                return null;
            }
            var o = e as Dictionary<string, object>;
            if (o == null || o.Count != 1)
            {
                Problem(where, "an expression is a value or an object with one operator");
                return null;
            }
            KeyValuePair<string, object> only = o.First();
            string op = only.Key;
            object arg = only.Value;
            switch (op)
            {
                case "var":
                {
                    string name = arg as string;
                    if (name == null || (!scope.Graph.Contains(name) && !scope.Local.Contains(name)))
                    {
                        Problem(where, $"no variable \"{name}\" (graph variables, or a result named with \"as\" earlier)");
                        return null;
                    }
                    return new GraphExpr { Kind = ExprKind.Var, Name = name };
                }
                case "event":
                {
                    string name = arg as string;
                    if (name == null || !scope.Event.Contains(name))
                    {
                        Problem(where, $"this event has no value \"{name}\"; it has {Listed(scope.Event.OrderBy(v => v, StringComparer.Ordinal).ToArray())}");
                        return null;
                    }
                    return new GraphExpr { Kind = ExprKind.Event, Name = name };
                }
                case "get":
                {
                    var parts = arg as List<object>;
                    if (parts == null || parts.Count < 2)
                    {
                        Problem(where, "get is [value, key or index, ...]");
                        return null;
                    }
                    GraphExpr of = Expression(parts[0], where, scope);
                    var keys = new List<object>();
                    foreach (object k in parts.Skip(1))
                    {
                        if (k is string || (GraphValues.IsNumber(k) && GraphValues.Number(k) == Math.Floor(GraphValues.Number(k))))
                        {
                            keys.Add(k);
                        }
                        else
                        {
                            Problem(where, "get's keys are text or whole numbers");
                        }
                    }
                    return of == null ? null : new GraphExpr { Kind = ExprKind.Get, Args = new[] { of }, Keys = keys.ToArray() };
                }
            }
            ExprKind kind;
            if (!Operators.TryGetValue(op, out kind))
            {
                Problem(where, $"no operator \"{op}\"");
                return null;
            }
            if (kind == ExprKind.Not || kind == ExprKind.Length)
            {
                GraphExpr one = Expression(arg, where, scope);
                return one == null ? null : new GraphExpr { Kind = kind, Args = new[] { one } };
            }
            var list = arg as List<object>;
            bool listed = kind == ExprKind.And || kind == ExprKind.Or || kind == ExprKind.Join;
            if (list == null || (listed ? list.Count == 0 : list.Count != 2))
            {
                Problem(where, listed ? $"{op} takes a list" : $"{op} takes two values");
                return null;
            }
            var built = new List<GraphExpr>();
            foreach (object a in list)
            {
                GraphExpr one = Expression(a, where, scope);
                if (one != null)
                {
                    built.Add(one);
                }
            }
            return built.Count == list.Count ? new GraphExpr { Kind = kind, Args = built.ToArray() } : null;
        }

        private static readonly Dictionary<string, ExprKind> Operators = new Dictionary<string, ExprKind>(StringComparer.Ordinal)
        {
            ["eq"] = ExprKind.Eq, ["ne"] = ExprKind.Ne, ["lt"] = ExprKind.Lt, ["le"] = ExprKind.Le,
            ["gt"] = ExprKind.Gt, ["ge"] = ExprKind.Ge, ["add"] = ExprKind.Add, ["sub"] = ExprKind.Sub,
            ["mul"] = ExprKind.Mul, ["div"] = ExprKind.Div, ["contains"] = ExprKind.Contains,
            ["and"] = ExprKind.And, ["or"] = ExprKind.Or, ["join"] = ExprKind.Join,
            ["not"] = ExprKind.Not, ["length"] = ExprKind.Length,
        };

        // The library a name of this shape comes from, when nothing under that
        // first part is registered at all; it is also noted for the Mods screen,
        // which says "Needs: Inspector" before the graph's problems.
        private string Missing(string name)
        {
            string library = GraphLibraries.For(name, _world.OperationNames, _world.EventNames);
            if (library != null && !_graph.Needs.Contains(library))
            {
                _graph.Needs.Add(library);
            }
            return library;
        }

        private static int Whole(object raw)
        {
            if (!GraphValues.IsNumber(raw))
            {
                return 0;
            }
            double d = GraphValues.Number(raw);
            return d == Math.Floor(d) && d > int.MinValue && d < int.MaxValue ? (int)d : 0;
        }

        private static string Listed(string[] names) => names.Length > 0 ? string.Join(", ", names) : "none";

        // The names a run may read: the graph's variables, the results named
        // with "as" so far, and what the event hands over. "as" and "error" hold
        // for the rest of the handler, as they do in the editor's blocks.
        private sealed class Scope
        {
            internal readonly HashSet<string> Graph;
            internal readonly HashSet<string> Local = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> Event;

            internal Scope(IEnumerable<string> variables, IEnumerable<string> values)
            {
                Graph = new HashSet<string>(variables, StringComparer.Ordinal);
                Event = new HashSet<string>(values ?? new string[0], StringComparer.Ordinal);
            }
        }
    }
}
