using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace DragNWash.ModFramework
{
    /// <summary>Whether an operation changes anything.</summary>
    public enum OperationKind
    {
        /// <summary>Only looks: objects, values, logs, mods. Safe to offer to anyone.</summary>
        Read,
        /// <summary>Changes something in the game.</summary>
        Write,
    }

    /// <summary>
    /// Who may call an operation. A library says who its operation is for
    /// (<see cref="Operation.Audience"/>), and each door asks with its own flag:
    /// the console, the Bridge's page on this computer, an AI client over MCP,
    /// a graph. Since 1.4.0.
    /// </summary>
    [Flags]
    public enum OperationAudience
    {
        /// <summary>Nobody in particular: a call from inside the framework, which is not checked.</summary>
        None = 0,
        /// <summary>The console in the F1 window, on this computer.</summary>
        Console = 1,
        /// <summary>The Bridge's page on this computer (the code graph, the editor).</summary>
        Page = 2,
        /// <summary>An AI client over MCP.</summary>
        Mcp = 4,
        /// <summary>A graph of a data mod.</summary>
        Graphs = 8,
        /// <summary>Everyone above: the default of a newly registered operation.</summary>
        Anyone = Console | Page | Mcp | Graphs,
    }

    /// <summary>The kind of value an operation's parameter takes.</summary>
    public enum OperationType
    {
        /// <summary>Text. Vectors and colours travel as text, written as the Inspector's rows show them.</summary>
        String,
        /// <summary>A number.</summary>
        Number,
        /// <summary>true or false.</summary>
        Boolean,
    }

    /// <summary>One parameter of an operation.</summary>
    public sealed class OperationParameter
    {
        /// <summary>Its name, lower case (<c>path</c>, <c>max</c>).</summary>
        public string Name { get; set; }
        /// <summary>The kind of value.</summary>
        public OperationType Type { get; set; }
        /// <summary>True when a call must give it.</summary>
        public bool Required { get; set; }
        /// <summary>One line on what it is.</summary>
        public string Description { get; set; }
        /// <summary>The values it accepts, when only some are; null for any.</summary>
        public string[] Choices { get; set; }
    }

    /// <summary>
    /// Something a library can do, by name, with plain arguments: the one thing
    /// behind the console's <c>op</c>, the MCP tools of the Bridge and the blocks
    /// of node graphs (docs/API_PLAN.md). Register with <see cref="Operations.Register"/>.
    /// </summary>
    public sealed class Operation
    {
        /// <summary><c>library.noun.verb</c>, e.g. <c>inspector.member.get</c>.</summary>
        public string Name { get; internal set; }
        /// <summary>One line on what it does, for people and AI clients.</summary>
        public string Description { get; internal set; }
        /// <summary>Read or write.</summary>
        public OperationKind Kind { get; internal set; }
        /// <summary>GUID of the mod that registered it.</summary>
        public string Owner { get; internal set; }
        /// <summary>Its parameters.</summary>
        public IReadOnlyList<OperationParameter> Parameters { get; internal set; }
        /// <summary>One line on what it returns.</summary>
        public string Returns { get; internal set; }
        /// <summary>
        /// Who may call it: <see cref="OperationAudience.Anyone"/> unless the
        /// library narrows it right after <see cref="Operations.Register"/>. What
        /// shows the game's own code is <c>Console | Page</c>, so no AI client and
        /// no graph gets it (docs/CODE_GRAPH.md). Since 1.4.0.
        /// </summary>
        public OperationAudience Audience { get; set; } = OperationAudience.Anyone;
        /// <summary>
        /// True for a write that outlives the session: it changes a file, a save
        /// or a setting, and quitting the game does not undo it. Said in stronger
        /// words than a write on the Mods screen, and never offered to graphs.
        /// Since 1.4.0.
        /// </summary>
        public bool Lasting { get; set; }

        internal Func<OperationArgs, object> Run;
    }

    /// <summary>The arguments of one call, read by name.</summary>
    public sealed class OperationArgs
    {
        private readonly Dictionary<string, object> _values;

        internal OperationArgs(Dictionary<string, object> values, string caller)
        {
            _values = values;
            Caller = caller ?? "?";
        }

        /// <summary>
        /// Who asked: <c>console</c>, <c>page</c>, <c>mcp:&lt;client&gt;</c>,
        /// <c>graph:&lt;mod&gt;/&lt;file&gt;</c>. A write uses it to say who
        /// changed a value, and to tell one caller's changes from another's.
        /// Since 1.4.0.
        /// </summary>
        public string Caller { get; }

        /// <summary>True when the call gave this argument.</summary>
        public bool Has(string name) => _values.ContainsKey(name);

        /// <summary>The argument as text, or <paramref name="fallback"/>.</summary>
        public string String(string name, string fallback = null)
        {
            return _values.TryGetValue(name, out object v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : fallback;
        }

        /// <summary>The argument as a number, or <paramref name="fallback"/>.</summary>
        public double Number(string name, double fallback = 0)
        {
            return _values.TryGetValue(name, out object v) && v is double d ? d : fallback;
        }

        /// <summary>The argument as a whole number, or <paramref name="fallback"/>.</summary>
        public int Int(string name, int fallback = 0) => Has(name) ? (int)Math.Round(Number(name, fallback)) : fallback;

        /// <summary>The argument as true or false, or <paramref name="fallback"/>.</summary>
        public bool Bool(string name, bool fallback = false)
        {
            return _values.TryGetValue(name, out object v) && v is bool b ? b : fallback;
        }

        /// <summary>
        /// Said by a write operation while it runs: what it changed, and how to
        /// put it back. The caller gets it in <see cref="OperationResult.TakenBackBy"/>
        /// (a graph keeps them and runs them, newest first, when it is switched
        /// off, reloaded or fails), and the registry raises
        /// <see cref="Operations.Written"/> with it, so the Inspector's History
        /// can list the change beside the ones made by hand. Say it once, after
        /// the change is made; a write that cannot be put back says nothing and
        /// is never offered to graphs. Since 1.4.0.
        /// </summary>
        /// <param name="label">What changed, for people: <c>Dragon/Body.enabled</c>.</param>
        /// <param name="undo">Puts it back. It must not throw.</param>
        /// <param name="before">The value before, as text, for the History.</param>
        /// <param name="after">The value now, as text.</param>
        public void TakeBack(string label, Action undo, string before = null, string after = null)
        {
            if (undo == null)
            {
                return;
            }
            TakeBack(label, () => { undo(); return true; }, before, after);
        }

        /// <summary>
        /// The same, for a write that can find its change already gone: the
        /// object destroyed, or somebody else's value in its place. It returns
        /// true when it put the value back and false when it left what it found
        /// alone, so a caller counting what it undid counts changes, not tries.
        /// Since 1.4.0.
        /// </summary>
        public void TakeBack(string label, Func<bool> undo, string before = null, string after = null)
        {
            if (undo == null)
            {
                return;
            }
            Label = label ?? "";
            Undo = undo;
            Before = before;
            After = after;
        }

        internal string Label, Before, After;
        internal Func<bool> Undo;
    }

    /// <summary>What a call gave back.</summary>
    public sealed class OperationResult
    {
        /// <summary>True when the operation ran without error.</summary>
        public bool Ok { get; internal set; }
        /// <summary>
        /// What it returned: null, a string, a number, a bool, or lists
        /// (<see cref="IList"/>) and objects (<see cref="IDictionary{String, Object}"/>) of these.
        /// </summary>
        public object Value { get; internal set; }
        /// <summary>Why it failed, for people.</summary>
        public string Error { get; internal set; }
        /// <summary>
        /// Put back what this call changed, when the write said how
        /// (<see cref="OperationArgs.TakeBack(string, Action, string, string)"/>); null for everything else.
        /// Since 1.4.0.
        /// </summary>
        public OperationTakeBack TakenBackBy { get; internal set; }

        /// <summary>The result as JSON: the value, or <c>{"error": "..."}</c>.</summary>
        public string ToJson(bool indented = false) => Ok ? Operations.ToJson(Value, indented) : Operations.ToJson(new Dictionary<string, object> { ["error"] = Error }, indented);
    }

    /// <summary>
    /// One change a write operation made, and how to put it back. Since 1.4.0.
    /// </summary>
    public sealed class OperationTakeBack
    {
        /// <summary>The operation that made it.</summary>
        public string Operation { get; internal set; }
        /// <summary>Who asked for it: <c>console</c>, <c>page</c>, <c>graph:&lt;mod&gt;/&lt;file&gt;</c>.</summary>
        public string Caller { get; internal set; }
        /// <summary>What changed, for people.</summary>
        public string Label { get; internal set; }
        /// <summary>The value before and the value now, as text; either may be null.</summary>
        public string Before { get; internal set; }
        /// <summary>The value before and the value now, as text; either may be null.</summary>
        public string After { get; internal set; }

        internal Func<bool> Undo;

        /// <summary>
        /// Puts the change back. Runs on the main thread. True when the value
        /// was put back; false when there was nothing to put back - the object
        /// is gone, or somebody else wrote the member after this write did - and
        /// false when putting it back threw.
        /// </summary>
        public bool Run()
        {
            try
            {
                return Undo();
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"[op] Putting back {Label} ({Operation}) failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// The registry of operations: each library registers what it can do under a
    /// name, with a description and plain parameters, and anyone can call it by
    /// name (the console's <c>op</c>, the Bridge's MCP tools, node graphs).
    /// Calls run on the main thread; every call is logged with who made it.
    /// Experimental. Since 1.4.0.
    /// </summary>
    public static partial class Operations
    {
        private static readonly Dictionary<string, Operation> Registered = new Dictionary<string, Operation>(StringComparer.Ordinal);
        private static readonly Queue<Action> MainThreadQueue = new Queue<Action>();
        private static Thread _mainThread;

        /// <summary>Largest JSON a call returns, in characters; longer results are an error asking for a narrower call.</summary>
        public const int MaxResultChars = 200000;

        internal static void Install()
        {
            _mainThread = Thread.CurrentThread;
            ModReload.Unloading += (guid, assembly) => { RemoveOwner(guid); RemoveOwnerEvents(guid); };
        }

        /// <summary>
        /// Registers an operation. The name is <c>library.noun.verb</c>, lower
        /// case; a second registration of a name is refused and logged. The
        /// operation goes when its owner is reloaded or unloaded.
        /// </summary>
        /// <param name="ownerGuid">BepInEx GUID of the mod registering it.</param>
        /// <param name="name">e.g. <c>saves.flags.list</c>.</param>
        /// <param name="description">One line on what it does.</param>
        /// <param name="kind">Read (changes nothing) or Write.</param>
        /// <param name="returns">One line on what it returns.</param>
        /// <param name="run">Runs on the main thread; returns null, strings, numbers, bools, lists and string-keyed dictionaries of these. Throw to fail with a message.</param>
        /// <param name="parameters">Its parameters.</param>
        /// <returns>The operation, or null when the name is taken.</returns>
        public static Operation Register(string ownerGuid, string name, string description, OperationKind kind, string returns, Func<OperationArgs, object> run, params OperationParameter[] parameters)
        {
            if (string.IsNullOrEmpty(ownerGuid) || string.IsNullOrEmpty(name) || run == null)
            {
                throw new ArgumentException("An operation needs its owner's GUID, a name and a function.");
            }
            if (name.Any(c => char.IsWhiteSpace(c) || char.IsUpper(c)))
            {
                throw new ArgumentException($"Operation names are lower case with no spaces: \"{name}\".", nameof(name));
            }
            var op = new Operation
            {
                Name = name,
                Description = description ?? "",
                Kind = kind,
                Owner = ownerGuid,
                Returns = returns ?? "",
                Parameters = (parameters ?? new OperationParameter[0]).Where(p => p != null).ToList(),
                Run = run,
            };
            lock (Registered)
            {
                if (Registered.TryGetValue(name, out Operation existing))
                {
                    ModFramework.Log.LogWarning($"[op] {ownerGuid} tried to register {name}, already registered by {existing.Owner}.");
                    return null;
                }
                Registered[name] = op;
            }
            return op;
        }

        /// <summary>A parameter, for <see cref="Register"/>.</summary>
        public static OperationParameter Parameter(string name, OperationType type, string description, bool required = false, params string[] choices)
        {
            return new OperationParameter { Name = name, Type = type, Description = description, Required = required, Choices = choices != null && choices.Length > 0 ? choices : null };
        }

        /// <summary>Every operation, by name.</summary>
        public static IReadOnlyList<Operation> All
        {
            get
            {
                lock (Registered)
                {
                    return Registered.Values.OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
                }
            }
        }

        /// <summary>The operation of that name, or null.</summary>
        public static Operation Find(string name)
        {
            lock (Registered)
            {
                return name != null && Registered.TryGetValue(name, out Operation op) ? op : null;
            }
        }

        internal static void RemoveOwner(string guid)
        {
            lock (Registered)
            {
                foreach (string name in Registered.Values.Where(o => o.Owner == guid).Select(o => o.Name).ToList())
                {
                    Registered.Remove(name);
                }
            }
        }

        /// <summary>
        /// Calls an operation now. Main thread only (use <see cref="Call"/>
        /// from anywhere else). <paramref name="caller"/> names who asked
        /// ("console", "mcp", a graph) in the log. Arguments are checked
        /// against the parameters: text is turned into numbers and true/false
        /// where the parameter says so.
        /// </summary>
        public static OperationResult CallNow(string name, IDictionary<string, object> args, string caller, OperationAudience asking = OperationAudience.None, bool measureResult = true)
        {
            if (_mainThread != null && Thread.CurrentThread != _mainThread)
            {
                return Fail("Operations run on the main thread; use Operations.Call from other threads.");
            }
            Operation op = Find(name);
            if (op == null)
            {
                return Fail($"No operation named \"{name}\". \"op\" in the console lists them.");
            }
            // A door says who it is; a call from inside the framework says nothing
            // and is not checked.
            if (asking != OperationAudience.None && (op.Audience & asking) == 0)
            {
                return Fail($"{name} is not offered here.");
            }
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (OperationParameter p in op.Parameters)
            {
                if (args == null || !args.TryGetValue(p.Name, out object raw) || raw == null)
                {
                    if (p.Required) return Fail($"{name} needs {p.Name} ({p.Description}).");
                    continue;
                }
                if (!Convert(raw, p, out object value, out string error)) return Fail($"{name}: {p.Name} {error}.");
                values[p.Name] = value;
            }
            if (args != null)
            {
                foreach (string key in args.Keys)
                {
                    if (op.Parameters.All(p => p.Name != key)) return Fail($"{name} has no parameter {key}. It takes: {string.Join(", ", op.Parameters.Select(p => p.Name).ToArray())}.");
                }
            }
            OperationResult result;
            var call = new OperationArgs(values, caller);
            try
            {
                object value = op.Run(call);
                // The cap is for what leaves this computer (the Bridge); a caller
                // that keeps the result in memory, a graph, skips it, and turning
                // a long result into JSON with it.
                string json = measureResult ? ToJson(value) : null;
                result = json != null && json.Length > MaxResultChars
                    ? Fail($"The result is {json.Length} characters, more than {MaxResultChars}; ask for less (a narrower path, a smaller max).")
                    : new OperationResult { Ok = true, Value = value };
            }
            catch (Exception ex)
            {
                result = Fail((ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex).Message);
            }
            if (result.Ok && call.Undo != null)
            {
                result.TakenBackBy = new OperationTakeBack
                {
                    Operation = name,
                    Caller = caller ?? "?",
                    Label = call.Label,
                    Before = call.Before,
                    After = call.After,
                    Undo = call.Undo,
                };
                try
                {
                    Written?.Invoke(result.TakenBackBy);
                }
                catch (Exception ex)
                {
                    ModFramework.Log.LogWarning($"[op] A listener of Operations.Written threw on {name}: {ex.Message}");
                }
            }
            string argText = string.Join(" ", values.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            string line = $"[op] {caller ?? "?"}: {name}{(argText.Length > 0 ? " " + argText : "")} -> {(result.Ok ? "ok" : result.Error)}";
            if (op.Kind == OperationKind.Write) ModFramework.Log.LogInfo(line);
            else ModFramework.Log.LogDebug(line);
            return result;
        }

        /// <summary>
        /// Raised after a write operation that said how to put itself back
        /// (<see cref="OperationArgs.TakeBack(string, Action, string, string)"/>): what changed, who asked, the
        /// value before and the value now. The Inspector lists these in its
        /// History, so a change a graph made can be seen and undone there.
        /// Since 1.4.0.
        /// </summary>
        public static event Action<OperationTakeBack> Written;

        /// <summary>
        /// Calls an operation from any thread: it runs on the main thread at the
        /// next frame, and <paramref name="done"/> gets the result there.
        /// </summary>
        public static void Call(string name, IDictionary<string, object> args, string caller, Action<OperationResult> done, OperationAudience asking = OperationAudience.None, bool measureResult = true)
        {
            lock (MainThreadQueue)
            {
                MainThreadQueue.Enqueue(() =>
                {
                    OperationResult r = CallNow(name, args, caller, asking, measureResult);
                    try { done?.Invoke(r); }
                    catch (Exception ex) { ModFramework.Log.LogWarning($"[op] The callback for {name} threw: {ex.Message}"); }
                });
            }
        }

        // Main thread, every frame (the core's Update).
        internal static void Tick()
        {
            for (int i = 0; i < 32; i++)
            {
                Action next;
                lock (MainThreadQueue)
                {
                    if (MainThreadQueue.Count == 0) return;
                    next = MainThreadQueue.Dequeue();
                }
                next();
            }
        }

        private static OperationResult Fail(string error) => new OperationResult { Ok = false, Error = error };

        private static bool Convert(object raw, OperationParameter p, out object value, out string error)
        {
            error = null;
            value = null;
            string text = raw as string;
            switch (p.Type)
            {
                case OperationType.Number:
                    if (raw is double || raw is float || raw is int || raw is long) { value = System.Convert.ToDouble(raw, CultureInfo.InvariantCulture); return true; }
                    if (text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) { value = d; return true; }
                    error = "is a number";
                    return false;
                case OperationType.Boolean:
                    if (raw is bool b) { value = b; return true; }
                    switch (text?.Trim().ToLowerInvariant())
                    {
                        case "true": case "1": case "yes": case "on": value = true; return true;
                        case "false": case "0": case "no": case "off": value = false; return true;
                    }
                    error = "is true or false";
                    return false;
                default:
                    value = text ?? System.Convert.ToString(raw, CultureInfo.InvariantCulture);
                    if (p.Choices != null && !p.Choices.Contains((string)value, StringComparer.OrdinalIgnoreCase))
                    {
                        error = "is one of " + string.Join(", ", p.Choices);
                        return false;
                    }
                    return true;
            }
        }

        // ---- JSON ------------------------------------------------------------------

        /// <summary>
        /// A value as JSON: null, strings, numbers, bools, lists and string-keyed
        /// dictionaries; anything else is written as its text.
        /// </summary>
        public static string ToJson(object value, bool indented = false)
        {
            var sb = new StringBuilder();
            Write(sb, value, indented, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object v, bool indented, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: Quote(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case double d: sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString("R", CultureInfo.InvariantCulture)); return;
                case int _: case long _: case short _: case byte _: case uint _: case ulong _: sb.Append(System.Convert.ToString(v, CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> o:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> kv in o)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, indented, depth + 1);
                        Quote(sb, kv.Key);
                        sb.Append(indented ? ": " : ":");
                        Write(sb, kv.Value, indented, depth + 1);
                    }
                    if (!first) NewLine(sb, indented, depth);
                    sb.Append('}');
                    return;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, indented, depth + 1);
                        Write(sb, item, indented, depth + 1);
                    }
                    if (!first) NewLine(sb, indented, depth);
                    sb.Append(']');
                    return;
                }
            }
            Quote(sb, v is IFormattable fm ? fm.ToString(null, CultureInfo.InvariantCulture) : v.ToString());
        }

        private static void NewLine(StringBuilder sb, bool indented, int depth)
        {
            if (!indented) return;
            sb.Append('\n').Append(' ', depth * 2);
        }

        private static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
