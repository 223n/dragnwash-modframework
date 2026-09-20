using System;
using System.Collections.Generic;

namespace DragNWash.ModFramework.Graphs
{
    // The limits, in one place: the checker refuses a file that breaks one of
    // the first four, and the runner stops a run that reaches one of the rest.
    // docs/GRAPHS.md, "Checking, once, at load" and "Running".
    internal static class GraphLimits
    {
        internal const int Format = 1;
        internal const int MaxStatements = 2000;
        internal const int MaxDepth = 32;
        internal const long MaxFileBytes = 256 * 1024;
        internal const int MaxLoop = 1000;
        internal const double MaxWait = 600;
        internal const int MaxStepsPerRun = 10000;
        internal const int MaxRunsPerGraph = 8;
        internal const int FailuresBeforeOff = 3;
        internal const double MinEvery = 0.5;
        // A call slower than this is logged at Debug with its graph, as a slow
        // GameEvents handler is: the budget cannot cut a call in half, so a slow
        // operation is the one thing a graph can do that shows in a frame.
        internal const double SlowCallMs = 5;
        // Lines a graph may write in a second; the rest are counted, not written.
        internal const int LogLinesPerSecond = 20;
    }

    // One graph file: what was read from it, what it may do, and how it is
    // going this session.
    internal sealed class Graph
    {
        internal string ModGuid;
        internal string ModName;
        internal string Path;
        // "graphs/scene-notes.json", as the log, the console and the Mods screen name it.
        internal string File;
        internal string Name;
        internal string Description;

        internal Dictionary<string, object> FirstValues = new Dictionary<string, object>(StringComparer.Ordinal);
        internal List<GraphHandler> Handlers = new List<GraphHandler>();

        // Empty when the file may run. A file with a problem does not run at all.
        internal List<string> Problems = new List<string>();
        // The operations it names, by name, and whether each changes anything.
        internal SortedDictionary<string, bool> Uses = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        // Libraries an operation it names belongs to but which are not installed.
        internal List<string> Needs = new List<string>();

        // ---- while the game runs ----
        internal Dictionary<string, object> Variables = new Dictionary<string, object>(StringComparer.Ordinal);
        internal List<GraphRun> Runs = new List<GraphRun>();
        internal List<GraphTakeBack> TakeBacks = new List<GraphTakeBack>();
        internal int Failures;
        internal int RunsStarted;
        internal bool Stopped;
        internal string StoppedWhy;
        // Set while an operation it uses is gone with its library (a reload).
        internal string Waiting;
        internal double LogWindow;
        internal int LogLines;
        internal int LogsLeftOut;

        internal bool Ok => Problems.Count == 0;

        internal bool CanRun => Ok && !Stopped && Waiting == null;

        internal string Where => (ModName ?? ModGuid) + " " + File;

        internal void Reset()
        {
            Variables = new Dictionary<string, object>(FirstValues, StringComparer.Ordinal);
            Runs.Clear();
            Failures = 0;
            RunsStarted = 0;
            Stopped = false;
            StoppedWhy = null;
            LogsLeftOut = 0;
            foreach (GraphHandler h in Handlers)
            {
                h.NextDue = 0;
            }
        }
    }

    internal sealed class GraphHandler
    {
        internal string Id;
        internal string Event;
        internal GraphExpr When;
        // "queue" starts another run while one is going; "skip" (the default) does not.
        internal bool Queue;
        // timer.every: how often, in seconds. key.pressed: which key.
        internal double Seconds;
        internal string Key;
        internal List<GraphStmt> Do = new List<GraphStmt>();
        // timer.every: when this handler is next due, in the clock's seconds.
        internal double NextDue;
    }

    // ---- statements ---------------------------------------------------------

    internal abstract class GraphStmt
    {
        internal string Id;
    }

    internal sealed class CallStmt : GraphStmt
    {
        internal string Operation;
        internal List<KeyValuePair<string, GraphExpr>> Args = new List<KeyValuePair<string, GraphExpr>>();
        internal string As;
        internal List<GraphStmt> OnError;
    }

    internal sealed class SetStmt : GraphStmt
    {
        internal string Name;
        internal GraphExpr To;
    }

    internal sealed class IfStmt : GraphStmt
    {
        internal GraphExpr Test;
        internal List<GraphStmt> Then = new List<GraphStmt>();
        internal List<GraphStmt> Else = new List<GraphStmt>();
    }

    internal sealed class WaitStmt : GraphStmt
    {
        internal double Seconds;
    }

    internal sealed class RepeatStmt : GraphStmt
    {
        internal int Times;
        internal List<GraphStmt> Do = new List<GraphStmt>();
    }

    internal sealed class WhileStmt : GraphStmt
    {
        internal GraphExpr Test;
        internal int Max;
        internal List<GraphStmt> Do = new List<GraphStmt>();
    }

    internal sealed class EachStmt : GraphStmt
    {
        internal GraphExpr Items;
        internal string As;
        internal int Max;
        internal List<GraphStmt> Do = new List<GraphStmt>();
    }

    internal sealed class LogStmt : GraphStmt
    {
        internal GraphExpr Text;
        internal GraphLevel Level = GraphLevel.Info;
    }

    internal sealed class StopStmt : GraphStmt
    {
    }

    // ---- expressions --------------------------------------------------------

    internal enum ExprKind
    {
        Literal,
        Var,
        Event,
        Get,
        Eq, Ne, Lt, Le, Gt, Ge,
        Add, Sub, Mul, Div,
        Contains,
        And, Or, Join,
        Not, Length,
    }

    // One operator with its arguments, or a plain value. Small and flat: an
    // expression is read many times a frame, so it holds no dictionaries.
    internal sealed class GraphExpr
    {
        internal ExprKind Kind;
        internal object Literal;
        // Var, Event: the name.
        internal string Name;
        internal GraphExpr[] Args = new GraphExpr[0];
        // Get: the keys and indexes after the first argument.
        internal object[] Keys = new object[0];

        internal static GraphExpr Value(object literal) => new GraphExpr { Kind = ExprKind.Literal, Literal = literal };
    }
}
