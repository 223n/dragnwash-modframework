# Graphs: building with blocks and nodes

[日本語](GRAPHS.ja.md)

> **Designed, not built** (2026-09-20). Stage 4 of the [API plan](API_PLAN.md), on the branch `experimental/graphs`. The research done without the game is in [Research](#research); a checker and a small interpreter written for that research are in `tools/graphs.py`. The open decisions for the owner are at the end.

A graph is a small mod with no code: *when this happens, do these things*. "When a scene loads, wait a second, look at what it has, write a line to the log." It is a JSON file in a folder with a `mod.json`, like an [overrides mod (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Overrides), and it can only call the [operations](API_PLAN.md) the libraries registered, the same ones the console's `op` and the Bridge offer. People make graphs on the Bridge's page, as blocks (as in Scratch) or as nodes; both are views of the same file.

## What the plan already decided

From the [plan's decisions](API_PLAN.md#decisions-2026-09-19):

- **No free scripting.** A graph calls registered operations only: no methods by name, no reflection, no files, no network of its own. What a graph can do is exactly what the operations it names allow.
- **The editor lives outside the game**, on the page the Bridge serves on this computer ([CODE_GRAPH.md](CODE_GRAPH.md)); the game only runs graphs.
- **Shipped like overrides**: a folder with `mod.json` and `graphs/*.json`, no DLL, shown and switched off on the Mods screen. Overrides and graphs share that loader.
- **Safe to run beside anything**: a time budget each frame, a graph that fails three times in a row switched off (as [`GameEvents`](../src/DragNWash.ModFramework/GameEvents.cs) handlers are), and the Mods screen lists the operations each graph uses before it runs.

## The graph file

```
BepInEx/plugins/<Mod>/mod.json           name, authors, description, version (as for overrides)
BepInEx/plugins/<Mod>/graphs/*.json      one graph per file
BepInEx/plugins/<Mod>/overrides/*.json   optional: a mod can have both
```

A graph file is one JSON object:

| Key | | What |
|---|---|---|
| `format` | required | `1`. A reader refuses a file of a higher format and says to update the framework, as the Overrides library does. |
| `name`, `description` | optional | For the Mods screen and the editor; the file name when left out. |
| `variables` | optional | The graph's variables and their first values (text, a number, true/false or null). They live while the game runs; nothing is saved. |
| `on` | required | The events it answers: a list of **handlers**. |
| `layout` | optional | Where the editor drew each block or node: `{ "<id>": [x, y] }`. The game never reads it. |

Any other key is an error, so a typo is found at once rather than ignored.

**Every handler and statement has an `id`**, unique in the file (the editor makes short ones: `h1`, `s12`). Errors, the live log, the Mods screen and `layout` all point at a statement by its id, so they stay right when blocks are moved.

### Handlers: the events

`{ "id": "h1", "event": "scene.loaded", "when": <expression>, "overlap": "skip", "do": [ statements ] }`

- `when` (optional) is a filter: the run starts only when it is true.
- `overlap`: what happens when the event comes again while a run of this handler is still going (waiting, say). `skip` (the default) ignores the new one; `queue` starts another run, up to 8 runs of the graph at once.

The events a graph can answer are those the libraries raise today, plus two of the Graphs library's own:

| Event | Values it hands over | Raised by |
|---|---|---|
| `game.started` | | Core, `GameEvents.OnGameStarted` |
| `scene.loaded` | `scene`, `mode` | Core, `GameEvents.OnSceneLoaded` |
| `scene.unloaded` | `scene` | Core, `GameEvents.OnSceneUnloaded` |
| `dialogue.node.started` | `node` | Dialogue, `GameDialogue.NodeStarted` |
| `dialogue.line.showing` | `line_id`, `speaker`, `text` | Dialogue, `GameDialogue.LineShowing` |
| `dialogue.option.showing` | `line_id`, `text` | Dialogue, `GameDialogue.OptionShowing` |
| `saves.written` | `slot` | Flags and saves, `GameSaves.SaveWritten` |
| `timer.every` (new) | | Graphs: every `seconds` (0.5 or more), real time |
| `key.pressed` (new) | `key` | Graphs: the `key` given (a key name such as `F6`) |

`GameEvents.OnQuitting` is left out: a run cannot wait while the game quits, and nothing a graph could do then would be seen. There is no per-frame event, as in `GameEvents`; `timer.every` at 0.5 seconds is the fastest.

A run does not start inside the event: the values are copied and the run starts at the Graphs library's next `Update`. So a graph never runs in the middle of another library's hook (a dialogue line being shown, a scene half made), and a slow graph cannot slow the event down for other mods. The price is one frame: a graph cannot change a line *before* it appears. That is a text rewriter's job (`GameText.AddRewriter`), not a graph's.

### Statements

A statement is an object with an `id` and exactly one of these keys:

| Statement | Written | What |
|---|---|---|
| call | `{"call": "saves.flags.list", "args": {"slot": ...}, "as": "flags", "onError": [...]}` | Calls an operation. The name is written out, never computed, so what the graph can call is known before it runs. `args` are expressions; `as` keeps the result for later statements in this run. Without `onError` a failed call fails the run; with it, the statements in `onError` run instead, with the message in `error`. |
| set | `{"set": "count", "to": <expression>}` | Sets a graph variable (one listed in `variables`). |
| if | `{"if": <expression>, "then": [...], "else": [...]}` | |
| wait | `{"wait": 1.5}` | Seconds of real time (the game's pause and time scale do not stretch it), 0 to 600; `0` is the next frame. |
| repeat | `{"repeat": 5, "do": [...]}` | A fixed number of times, 1 to 1000. |
| while | `{"while": <expression>, "max": 100, "do": [...]}` | While true, at most `max` rounds (1 to 1000, required). Still true after `max` rounds fails the run: a loop that never ends is a mistake, not something to carry on with quietly. |
| each | `{"each": <expression>, "as": "item", "max": 50, "do": [...]}` | Once for every item of a list (a result, usually). A list longer than `max` fails the run. |
| log | `{"log": <expression>, "level": "Info"}` | A line in the log under the graph's mod (`Info`, `Warning`, `Error`). At most 20 lines a second per graph; the rest are counted, not written. |
| stop | `{"stop": true}` | Ends this run (not the graph). |

There is no `goto`, no statement that makes another handler run, and no recursion: every run ends, and the limits say how soon.

### Expressions

An expression is a plain value (`"text"`, `3`, `true`, `null`) or an object with one operator:

| | |
|---|---|
| `{"var": "name"}` | A graph variable, or a result named with `as` earlier in the run (`error` in `onError`). |
| `{"event": "scene"}` | A value the event handed over. |
| `{"get": [<expression>, "key", 0, ...]}` | A field of an object, an item of a list (0 first, -1 last); nothing when it is not there. |
| `eq`, `ne`, `lt`, `le`, `gt`, `ge` | `{"gt": [a, b]}`. Numbers compare as numbers, anything else as text. |
| `add`, `sub`, `mul`, `div` | Numbers only; anything else, or dividing by zero, fails the run. |
| `and`, `or` | `{"and": [a, b, ...]}` |
| `not` | `{"not": a}` |
| `join` | `{"join": ["Scene ", {"event": "scene"}]}`: text, lists and objects written as JSON. |
| `contains` | `{"contains": [a, b]}`: text in text (ignoring case), or an item in a list. |
| `length` | `{"length": a}`: of text, a list or an object; 0 for anything else. |

Values are the ones operations already return: null, text, numbers, true/false, lists and objects. False, 0, `""`, null and an empty list or object count as false. Arguments are converted by the registry as for the console (`"3"` for a number parameter works).

This is close to JSON Logic's shape (one operator per object, arguments in a list), which keeps the file readable by people and easy to check.

### Versions

`format` is 1. A new statement, operator or handler key is a new format: an old library meets a file it cannot run and says so on the Mods screen, rather than running half of it. Keys the game ignores (`layout`) and new events or operations do not need a new format: a missing event or operation is found when the file is checked against the running game (below).

### An example

What the operations can do today: look. This graph writes a note to the log when a scene loads, warns about roots with many children, and counts a save's flags when the game saves. It uses the Inspector's operations, so it needs the Inspector installed; the Mods screen says so.

<!-- example-graph -->
```json
{
  "format": 1,
  "name": "Scene notes",
  "description": "Writes to the log what each scene has at its top level, and how many flags each save holds.",
  "variables": { "scenes": 0 },
  "on": [
    {
      "id": "h1",
      "event": "scene.loaded",
      "do": [
        { "id": "s1", "set": "scenes", "to": { "add": [{ "var": "scenes" }, 1] } },
        { "id": "s2", "wait": 1 },
        { "id": "s3", "call": "inspector.objects.children", "as": "roots" },
        { "id": "s4", "log": { "join": ["Scene ", { "event": "scene" }, " (", { "var": "scenes" }, " this session): ", { "length": { "var": "roots" } }, " root objects"] } },
        { "id": "s5", "each": { "var": "roots" }, "as": "root", "max": 200, "do": [
          { "id": "s6", "if": { "gt": [{ "get": [{ "var": "root" }, "children"] }, 50] }, "then": [
            { "id": "s7", "level": "Warning", "log": { "join": [{ "get": [{ "var": "root" }, "path"] }, " has ", { "get": [{ "var": "root" }, "children"] }, " children"] } }
          ] }
        ] }
      ]
    },
    {
      "id": "h2",
      "event": "saves.written",
      "do": [
        { "id": "s8", "call": "saves.flags.list", "args": { "slot": { "event": "slot" } }, "as": "flags",
          "onError": [{ "id": "s9", "level": "Warning", "log": { "join": ["Could not read ", { "event": "slot" }, ": ", { "var": "error" }] } }] },
        { "id": "s10", "log": { "join": [{ "event": "slot" }, " was saved: ", { "length": { "var": "flags" } }, " flags"] } }
      ]
    }
  ]
}
```

`python tools/graphs.py --test` checks this very block against the operations in `src/` and runs it with stand-ins for the game, so the example cannot drift from the registry.

In blocks, handler `h1` reads as one stack under a hat block:

```
when scene loaded
  set scenes to (scenes + 1)
  wait 1 seconds
  roots = inspector › objects › children
  log "Scene " (scene) " (" (scenes) " this session): " (length of roots) " root objects"
  for each root in roots (at most 200)
    if (root › children) > 50
      log warning (root › path) " has " (root › children) " children"
```

## What is missing

Today (2026-09-20) the registry has **28 operations: 27 read, 1 write**. Leaving out the 5 page-only ones (`code.*`, which show the game's code) and the Bridge's `bridge.page.open` (the only write), **a graph could call 22, all reads**. So a graph can look and write to the log, and nothing else. That is useful for mod makers (reports, checks, "tell me when this happens"), not yet for players.

| Library | Operations a graph could call |
|---|---|
| Core | `mods.list`, `mods.network`, `game.info`, `scene.list` |
| Tool window | `log.read` |
| Assets | `assets.textures.list`, `assets.materials.list`, `assets.meshes.list`, `assets.replacements.list`, `assets.fonts.language` |
| Dialogue | `dialogue.current`, `dialogue.recent` |
| Text | `text.rewriters`, `text.shown` |
| Flags and saves | `saves.list`, `saves.flags.list`, `saves.flags.get` |
| Inspector | `inspector.objects.find`, `inspector.objects.children`, `inspector.components.list`, `inspector.member.get`, `inspector.selection.get` |

### The first writes graphs need

In the order they would be most use, each with how it is taken back:

| Operation | What | Taken back by |
|---|---|---|
| `objects.member.set` | A component's field or property, as one overrides row does (`path`, `component`, `index`, `member`, `private`, `value` as text, parsed by `OverrideValues`). | The value before, kept per graph, as `OverrideApplier.TakeBackAll` does for overrides. |
| `objects.material.set` | A material's property (an overrides row with `material`, `property`). | The same. |
| `objects.active.set` | Shows or hides an object. | The state before. |
| `saves.flags.set` | One flag in a save slot (`GameSaves.SetFlag`, which keeps a snapshot first). | The snapshot (the Saves tab's history). **Lasting**: it changes a file and outlives the session. |

The first three are what overrides already do, only at a moment of the graph's choosing instead of whenever the object appears. `saves.flags.set` is different in kind: it outlives the game, and takes effect only when the slot is loaded again (the Saves tab says the same). It should wait (decision 5).

What is missing beyond operations:

- **Events**: a level starting or ending, the player's actions, an object appearing. Each needs research into the game's code first (the code graph is the tool for it), and belongs to the library that hooks it.
- **A message to the player.** The only output is the log. A small notice (the roadmap's "a notice per mod on the Mods screen", or a line on screen) would need its own design.
- **Mods' own operations.** Drag'n Wash Localization could register `loc.line.get` and `loc.coverage` (the plan's example), and graphs would get them without any change here.

### How writes stay safe

1. **Kinds.** Every operation says *read* or *write*. Add a third mark, **lasting** (a write that outlives the session: files, saves), so the Mods screen can say "changes your saves" in words stronger than "changes values in the game". Graphs may not call lasting writes in the first version.
2. **Taken back.** A write operation hands the registry a way to undo it, through a new `OperationArgs.TakeBack(label, undo)`. The Graphs library keeps them per graph and runs them, newest first, when the graph is switched off, reloaded, or fails for the third time. A write that cannot be taken back is not offered to graphs.
3. **History.** The registry raises `Operations.Written` (operation, caller, label, before, after); the Inspector, when installed, lists those in its History with the caller (`graph:<mod>/<file>`), so a person can see and undo one write there. The Inspector's History is internal today; this is a small hook, not a move.
4. **Before it runs.** The Mods screen lists what each graph uses (below), writes first.
5. **Logged.** Every write is logged at Info with its caller, as the registry already does.

## Runtime: the Graphs library

A new library, **Graphs** (`DragNWash.ModFramework.Graphs`, `com.tomxv.dragnwash.modframework.graphs`), its own plugin, depending on the core. It reads graph files, checks them, runs them and lists them. It knows nothing about any operation: everything comes through `Operations`.

### Checking, once, at load

A file is checked completely before anything runs, against the operations registered *now* (after every plugin's `Awake`, at `ModFramework.Ready`):

- The shape: the keys, ids, statements and expressions above; at most 2,000 statements, nested at most 32 deep, a file at most 256 KB (the Overrides library reads up to 2 MB of values; a graph is smaller).
- Every `call` names an operation that exists, is not page-only and is offered to graphs; every required argument is there; a written-out argument has the right type and one of the allowed choices.
- Every `{"var"}` is a graph variable or named by an earlier `as`; every `{"event"}` is a value that event hands over.

A file with a problem does not run at all; its problems are listed on the Mods screen, as overrides' are. A graph whose operation belongs to a library that is not installed says *needs Inspector* instead of failing later.

### Running

- **Runs** are small stacks of positions (statement list, index, loop state), not threads or coroutines: the whole state of a run is data, so it can be paused at any statement, listed and stopped.
- **A time budget each frame**: all graphs together get **1 ms** per frame (`[Graphs] FrameBudgetMs`), shared round-robin between runs, the clock read every 16 steps. When the budget is used, runs go on at the next frame. An operation call cannot be cut in half: a call that takes longer than 5 ms is logged at Debug with its graph, as slow `GameEvents` handlers are.
- **Limits per run**: 10,000 steps; loops at most 1,000 rounds each (`max` is required); waits at most 600 seconds; at most 8 runs alive per graph.
- **Failure**: a call that fails (without `onError`), a limit reached, a type error. The run ends, the error is logged with the graph, the file and the statement id, and it counts. A run that ends normally resets the count.
- **Three failures in a row switch the graph off** for the session, its writes taken back, with a mark on the Mods screen through `GameHooks.Unavailable` (as `GameEvents` handlers). Other graphs of the same mod go on.
- **Owners**: every call is made as `graph:<mod GUID>/<file>` (the registry logs the caller). When a library is reloaded or unloaded (`ModReload.Unloading`), its operations go; the graphs that use them stop without it counting as a failure, and start again when the operations are back.
- **Reload**: `graphs reload` in the console, or a save from the page, reloads one file or all: runs stopped, writes taken back, variables reset, the file checked again and started. Nothing carries over, which keeps a reload the same as a fresh start.

### Data mods, one loader

Today the Overrides library finds its mods itself (`OverrideFiles.Scan`: a folder with `mod.json` and `overrides/`) and lists each with `ModFramework.RegisterDataMod`, and switching one off renames `mod.json` to `mod.json.disabled` at the next launch. A folder with both `overrides/` and `graphs/` must be **one** mod on the Mods screen, switched off once.

Two ways (decision 3):

- **A. The core reads `mod.json`.** A small `DataMods` in the core (next to `RegisterDataMod`, which is already there) finds the folders with `mod.json`, reads the manifest once and lists the mod; Overrides and Graphs each ask it for the folders that have their subfolder, and add a line to the mod's details (*12 values*, *2 graphs*). Neither library depends on the other.
- **B. Graphs depends on Overrides**, which becomes the data mod loader and hands Graphs the folders with `graphs/`. No core change, but a mod of graphs only would need the Overrides library too.

### Switching off, and the Mods screen

- The **mod** is switched off like any data mod: `mod.json.disabled` at the next launch.
- A **graphs page** in the mod's details (`ModFramework.AddModsPage`) lists each graph: its events, what it uses, whether it runs, its failures, and a **Stop for this session** button (writes taken back). There is no per-graph switch that is kept; a mod that wants graphs to be optional ships them as two mods.

### When mods meet

- **Two graphs write the same thing** (the same object and member): both writes happen, the later one wins, and the Mods screen notes both mods once (*Scene tweaks and Night mode both change Sun [Light] intensity*), as overrides do today (`SaidConflict`). When one graph is stopped, its take-back puts back the value *it* found, which may be the other graph's; so take-backs run newest first, and a take-back that finds a value it did not write leaves it alone and logs it.
- **An overrides row and a graph** on the same member: the same rule; the Overrides library writes when objects appear, the graph when it runs.
- **The same key** in two graphs (`key.pressed`): both run; the graphs page says which mods listen to that key. Keys the framework uses (F1) are refused at load.
- **Names**: graphs have no names others can call, so there is nothing to clash. Variables belong to their graph.

## The editor

On the Bridge's page, beside the code graph: a **Graphs** tab. It opens the same way (the F1 window's button, the Code Graph app on Windows, a one-time code and a cookie), so there is one page and one sign-in to keep.

- **Blocks** (as in Scratch): each handler is a hat block with a stack under it; `if`, `repeat`, `while`, `each` and `onError` are C-shaped blocks; expressions are round blocks dropped into slots. Operations come from the registry, grouped by library, each with its parameters as slots (a list for choices, a tick for true/false), its description as the tooltip, and a colour for its kind (writes stand out).
- **Nodes**: the same statements as boxes; flow runs down the edges (*then*, *else*, *each item*, *next*), and a result named with `as` is a wire from the call to where it is used. The file is a tree, so the node view keeps it one: a flow output has one wire, and loops are the loop nodes, not wires drawn back. What cannot be written as blocks cannot be drawn as nodes, so switching views never loses anything.
- **One file, two views.** Both edit the same JSON; `layout` keeps where blocks and nodes were drawn (by id). The page checks as you edit (the same rules as the game, with the operations it lists), and shows problems on the block.
- Drawn by the page's own code, as the code graph is: nothing fetched from the internet, no library shipped (decision 6).

### What the page needs from the Bridge

The page calls operations through its door (`POST /page/api/op`), so the Graphs library registers these, all **page-only** (never offered to MCP):

| Operation | Kind | What |
|---|---|---|
| `graphs.catalog` | read | Every operation a graph may call, with its parameters (type, required, choices, description), what it returns and its kind; and the events with their values. |
| `graphs.list` | read | The graph mods and graphs loaded: state, failures, uses. |
| `graphs.read` | read | One graph file's text. |
| `graphs.check` | read | Checks a graph given as text; the problems, by statement id. |
| `graphs.save` | write | Writes a graph into a data mod's `graphs/` (making the folder and `mod.json` for a new mod), then reloads it. |
| `graphs.run` | write | Starts one handler now with values given by the page (to try it without waiting for the event). |
| `graphs.stop` | write | Stops a graph for the session, its writes taken back. |
| `graphs.log` | read | The graph lines of the log after a given number, for the page's live log (polled every half second; the Bridge has no server-sent events). |

Two things change in the Bridge:

- **The page door refuses writes today** (`PageDoor.Call`: *No read operation named …*). It would accept writes that are **page-only**, and only those: the page may save and run graphs, but not call `objects.member.set` itself. MCP stays read-only and never sees `graphs.*` (decision 2).
- **Where it may save**: only into `BepInEx/plugins/<folder>/graphs/<name>.json`, where the folder is a data mod (has `mod.json`, no DLL) or is new, `<name>` is letters, digits, `-` and `_`, and the text is at most 256 KB and passes `graphs.check`. It never writes anywhere else, never overwrites a DLL mod's folder, and keeps the previous file as `<name>.json.bak`.

## Security and the player's view

What a graph **cannot** do, by construction: call a method by name, use reflection, read or write files, open a connection, start a program, call page-only operations (the game's code), call the Bridge's operations, run forever, or take more than its share of a frame. The only things it can touch are what registered operations do, and the only new thing it adds is when.

What the player sees **before it runs**, in the spirit of [Going online (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Going-online): the Mods screen's details for a graph mod say, for each graph,

```
Scene notes (graphs/scene-notes.json)
  Answers: scene loaded, save written
  Reads:   inspector.objects.children, saves.flags.list
  Changes: nothing
  Needs:   Inspector
```

with writes listed first and in the warning colour when there are any (*Changes: objects.member.set — changes values of objects in the game; taken back when switched off*), and a line for private members as overrides have (*changes private values of the game's scripts*), taken from the `private` argument where it is written out and assumed when it is computed.

- **Network**: no operation goes online today. If one ever does, its library declares it in `ModInfo.Network`, and a graph that calls it shows *goes online through <operation>* here.
- **Consent**: disclosure, not a prompt, the same as overrides (which change values without asking) and network use. Writes are taken back when the mod is switched off (decision 4).
- **Content policy**: a graph is its author's own work. Graphs hold names of objects, members and operations, not the game's text or code; a graph that logs dialogue lines writes them to the player's own log only. See [CONTENT_POLICY.md](CONTENT_POLICY.md).

## Research

Done 2026-09-20, without the game.

### The registry

Read from `src/` with `python tools/graphs.py inventory` (which parses the `Operations.Register` calls):

| Library | Read | Write | Page only | Graphs could call |
|---|---|---|---|---|
| Core | 4 | | | 4 |
| Tool window | 1 | | | 1 |
| Assets | 5 | | | 5 |
| Dialogue | 2 | | | 2 |
| Text | 2 | | | 2 |
| Flags and saves | 3 | | | 3 |
| Inspector | 10 | | 5 (`code.*`) | 5 |
| Bridge | | 1 (`bridge.page.open`) | | 0 |
| **All** | **27** | **1** | **5** | **22** |

The 22 match what MCP offers (read, not page-only). Parameters are few (0 to 5) and of three types (text, number, true/false), with choices on one (`log.read`'s `level`); so the editor's slots are simple, and `graphs.catalog` is small.

The events come from four places: `GameEvents` (4, of which 3 fit graphs), `GameDialogue` (3), `GameSaves.SaveWritten` (1). Other public events in the libraries (`DeveloperTools.Changed`, `ToolWindow.OpenChanged`, `ConsoleLog.Added`, `AssetReplacements.Changed`, `GameFonts.CharactersPrepared`, `ModReload.Unloading`) are about the framework, not the game, and are left out.

### How other editors keep their files

From general knowledge of them (their documentation was not read again for this record, so details may be out of date):

- **Scratch** keeps each sprite's blocks as one flat table keyed by block id; each block names its `next` and `parent`, its inputs point at other blocks by id, and top blocks carry `x`, `y`.
- **Blockly** saves JSON as a tree: a block holds its fields, its inputs holding the blocks inside, and `next` holding the block below; top blocks carry `x`, `y`.
- **Node-RED** keeps a flat list of nodes, each with an `id`, a type, `x`, `y` and `wires` (for each output, the ids it connects to); flows are free graphs, and loops through wires are allowed.

This design is a tree like Blockly's, with ids like Scratch's and positions apart (`layout`), because a tree makes the limits easy to check (every loop is a loop statement with a `max`; no wire can go back), reads well in a text editor and in a diff, and fits both views. Node-RED's free wiring is the one thing given up: a node that feeds two places at once, or a loop drawn as a wire, is not possible.

### Cost per step

A small C# benchmark (not kept in the repo) ran a tree interpreter of the same shape: statements compiled once from the JSON into objects, values boxed, variables in slots, and an operation call through a copy of the registry's steps (arguments into a dictionary, converted, the function run, the result turned into JSON for the size limit, the log line built). On this PC (Windows 11), on .NET Framework 4.8 and .NET 9:

| Step | .NET Framework 4.8 | .NET 9 |
|---|---|---|
| `set` + `if` with arithmetic and a comparison | 9 ns | 21 ns |
| the same, reading the clock every step | 26 ns | 26 ns |
| `join` of four parts | 256 ns | 168 ns |
| call of an operation returning a small object | 402 ns | 224 ns |
| the same without the JSON size check | 200 ns | 112 ns |
| call returning 100 rows (as `inspector.objects.children`) | **35,563 ns** | 14,494 ns |
| the same without the JSON size check | 142 ns | 75 ns |

- The game runs Unity's Mono, which was not measured; count on it being several times slower than .NET Framework. Even at ten times, a 1 ms budget holds thousands of plain steps and about a hundred calls a frame. The interpreter is not the cost; **the operations are** (walking a scene, reading a save file), and those are the same whoever calls them.
- **The JSON size check costs more than the call.** `Operations.CallNow` turns every result into JSON to enforce the 200,000-character cap, which matters for results sent over the Bridge but not for a graph that keeps the result in memory. Graphs should call without it (a list longer than an `each`'s `max` already fails) — a small change in the registry (an internal `CallNow` that skips the check, or a cap counted in items).
- Reading the clock every step costs as much as the step, so the budget is checked every 16 steps.

The Python sketch (`tools/graphs.py --test`) runs the example graph with stand-ins for the operations, refuses 8 broken graphs with the right message (a page-only operation, a missing argument, a `while` without `max`, an unknown variable, a value the event does not have, a choice that is not allowed, a newer format, an id used twice), switches off a graph after three failures, and stops an endless `while` at its `max`.

## Order of work

1. Research (above).
2. The registry: `lasting`, `OperationArgs.TakeBack`, `Operations.Written`, a call without the JSON check for graphs, an *audience* per operation (decision 1), and the event registry `Operations.RegisterEvent`, with each library raising its own events through it (decision 8).
3. The loader: data mods found once (decision 3); Overrides moved onto it, unchanged for players.
4. The Graphs library: reading, checking, running with the budget and limits, failures, take-backs, the console's `graphs`, the Mods screen's graphs page. Tried with graph files written by hand, reads only.
5. The first writes: `objects.member.set`, `objects.material.set`, `objects.active.set` (with take-backs and History).
6. The page: `graphs.*` operations, the page door's page-only writes, the Graphs tab with blocks, then nodes.
7. Docs: a wiki page for players and makers; the Overrides page mentions graphs.

## Decisions

Decided by the owner on 2026-09-20.

1. **Which operations graphs may call: each operation says who it is for.** `PageOnly` becomes an *audience* (console, page, MCP, graphs), so a library can offer an operation to people but not to graphs. `bridge.*` and `code.*` are not for graphs.
2. **The page writes through its own door, never MCP.** The page door accepts the page-only writes that saving and running graphs need; MCP stays read-only, and letting an AI client write a graph is a decision of its own, not taken here.
3. **One loader for data mods, in the core**, which already has `RegisterDataMod` and the `mod.json.disabled` switch. A mod of graphs alone needs no Overrides library, and neither library depends on the other.
4. **Graphs that write say so on the Mods screen**, as overrides and network use do; no prompt before a run, since every write is taken back when the mod is switched off.
5. **Lasting writes (saves) are not in the first version.** When they come, a graph that uses one says so in the warning colour, and the Saves tab's snapshot is its undo.
6. **The editor is drawn by our own code**, as the code graph is. Blockly is a large library to ship inside the Bridge, and the block set here is small (9 statements, 19 operators).
7. **The object writes live in the Overrides library**, named `objects.*`: it already has the value parsing, the finding of objects by path and the putting back, and players who install a graph mod have it, while the Inspector (a tool for mod makers) they do not. The Inspector's History joins through `Operations.Written`.
8. **Events are owned by the libraries that raise them, from the first version.** A small event registry beside the operations (`Operations.RegisterEvent`, raised by each library) replaces the hard-coded table: Dialogue owns the dialogue events, Flags and saves its own, Graphs its `timer.every` and `key.pressed`, and a mod can add events of its own. The events in the table above stay as they are; they are registered by their libraries instead of listed in the Graphs library.
