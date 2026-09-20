using System;
using System.Collections;
using System.Collections.Generic;

namespace DragNWash.ModFramework.Graphs
{
    // Why a run ended badly. Caught by the runner, which logs it with the graph,
    // the file and the statement's id, and counts it.
    internal sealed class GraphFailure : Exception
    {
        internal GraphFailure(string message) : base(message)
        {
        }
    }

    // One run of one handler. Everything about it is data — which statements it
    // is inside, where in each, what a loop has left to do — so it can be
    // stopped at any statement, listed, and picked up again next frame. No
    // thread and no coroutine: a run only ever moves inside the runner's frame
    // budget, on the main thread.
    internal sealed class GraphRun
    {
        internal sealed class Position
        {
            internal List<GraphStmt> Statements;
            internal int At;
            internal Loop Loop;
        }

        internal sealed class Loop
        {
            // repeat and while: rounds left. each: the items and where it is.
            internal int Left;
            internal WhileStmt While;
            internal IList Items;
            internal int ItemAt;
            internal string As;
        }

        internal readonly Graph Graph;
        internal readonly GraphHandler Handler;
        internal readonly Dictionary<string, object> Event;
        internal readonly Dictionary<string, object> Locals = new Dictionary<string, object>(StringComparer.Ordinal);
        internal readonly List<Position> Stack = new List<Position>();
        internal int Steps;
        internal double Wake;
        internal bool Done;

        internal GraphRun(Graph graph, GraphHandler handler, Dictionary<string, object> values)
        {
            Graph = graph;
            Handler = handler;
            Event = values ?? new Dictionary<string, object>(StringComparer.Ordinal);
            Stack.Add(new Position { Statements = handler.Do });
        }
    }

    // Runs the graphs: starts runs when events come, moves them a little each
    // frame inside one shared time budget, counts failures and switches a graph
    // off after three in a row, and keeps what each graph changed so it can be
    // put back.
    internal sealed class GraphRunner
    {
        // Steps between two readings of the clock. Reading it costs about as
        // much as a plain step (docs/GRAPHS.md, "Cost per step"), so it is read
        // once a quantum instead of every step.
        private const int Quantum = 16;

        private readonly IGraphWorld _world;
        private int _cursor;

        internal readonly List<Graph> Graphs = new List<Graph>();

        internal GraphRunner(IGraphWorld world)
        {
            _world = world;
        }

        // ---- starting ---------------------------------------------------------

        /// <summary>
        /// Starts a run of every handler of every graph that answers this event.
        /// Called from the runner's own frame, never from inside the event.
        /// </summary>
        internal void Raise(string name, IDictionary<string, object> values)
        {
            foreach (Graph graph in Graphs)
            {
                if (!graph.CanRun)
                {
                    continue;
                }
                foreach (GraphHandler handler in graph.Handlers)
                {
                    if (handler.Event == name && Wanted(handler, values))
                    {
                        Start(graph, handler, values);
                    }
                }
            }
        }

        // The two events this library raises itself go to every graph that
        // listens, so each handler says which of them it meant: its own key, and
        // its own place in time.
        private bool Wanted(GraphHandler handler, IDictionary<string, object> values)
        {
            if (handler.Event == GraphEvents.Key)
            {
                object key;
                return values != null && values.TryGetValue("key", out key) &&
                       string.Equals(GraphValues.Text(key), handler.Key, StringComparison.OrdinalIgnoreCase);
            }
            if (handler.Event == GraphEvents.Timer)
            {
                double now = _world.Now;
                if (now < handler.NextDue)
                {
                    return false;
                }
                handler.NextDue = now + handler.Seconds;
                return true;
            }
            return true;
        }

        internal void Start(Graph graph, GraphHandler handler, IDictionary<string, object> values)
        {
            if (!graph.CanRun)
            {
                return;
            }
            int alive = 0;
            bool already = false;
            foreach (GraphRun other in graph.Runs)
            {
                if (other.Done)
                {
                    continue;
                }
                alive++;
                already |= other.Handler == handler;
            }
            if (already && !handler.Queue)
            {
                return;
            }
            if (alive >= GraphLimits.MaxRunsPerGraph)
            {
                _world.Write(GraphLevel.Debug, $"{graph.Where} {handler.Id}: {GraphLimits.MaxRunsPerGraph} runs are already going, so {handler.Event} was let go.");
                return;
            }
            var run = new GraphRun(graph, handler, Copy(values));
            if (handler.When != null)
            {
                try
                {
                    if (!GraphValues.Truthy(Value(handler.When, run)))
                    {
                        return;
                    }
                }
                catch (GraphFailure ex)
                {
                    Fail(run, ex.Message);
                    return;
                }
            }
            graph.Runs.Add(run);
            graph.RunsStarted++;
        }

        // The event's values belong to whoever raised them; a run keeps its own copy.
        private static Dictionary<string, object> Copy(IDictionary<string, object> values)
        {
            var copy = new Dictionary<string, object>(StringComparer.Ordinal);
            if (values != null)
            {
                foreach (KeyValuePair<string, object> kv in values)
                {
                    copy[kv.Key] = kv.Value;
                }
            }
            return copy;
        }

        // ---- the frame --------------------------------------------------------

        /// <summary>
        /// Moves the runs along for at most <paramref name="budgetMs"/>
        /// milliseconds, round-robin, so one busy graph cannot hold the others up
        /// and no graph can hold the game up.
        /// </summary>
        internal void Frame(double budgetMs)
        {
            double now = _world.Now;
            var ready = new List<GraphRun>();
            foreach (Graph graph in Graphs)
            {
                foreach (GraphRun run in graph.Runs)
                {
                    if (!run.Done && run.Wake <= now)
                    {
                        ready.Add(run);
                    }
                }
            }
            if (ready.Count == 0)
            {
                Sweep();
                return;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int from = _cursor % ready.Count;
            bool moving = true;
            while (moving)
            {
                moving = false;
                for (int i = 0; i < ready.Count; i++)
                {
                    GraphRun run = ready[(from + i) % ready.Count];
                    if (run.Done || run.Wake > now)
                    {
                        continue;
                    }
                    moving = true;
                    for (int step = 0; step < Quantum && !run.Done && run.Wake <= now; step++)
                    {
                        try
                        {
                            Step(run);
                        }
                        catch (GraphFailure ex)
                        {
                            Fail(run, ex.Message);
                        }
                        catch (Exception ex)
                        {
                            Fail(run, ex.Message);
                        }
                    }
                    if (watch.Elapsed.TotalMilliseconds >= budgetMs)
                    {
                        // The rest go on next frame, starting one further along.
                        _cursor++;
                        Sweep();
                        return;
                    }
                }
            }
            _cursor++;
            Sweep();
        }

        private void Sweep()
        {
            foreach (Graph graph in Graphs)
            {
                graph.Runs.RemoveAll(r => r.Done);
            }
        }

        private void Step(GraphRun run)
        {
            run.Steps++;
            if (run.Steps > GraphLimits.MaxStepsPerRun)
            {
                throw new GraphFailure($"more than {GraphLimits.MaxStepsPerRun} steps in one run");
            }
            GraphRun.Position at = run.Stack[run.Stack.Count - 1];
            if (at.At >= at.Statements.Count)
            {
                if (LoopAgain(run, at))
                {
                    return;
                }
                run.Stack.RemoveAt(run.Stack.Count - 1);
                if (run.Stack.Count == 0)
                {
                    run.Done = true;
                    // A run that ends properly clears what went wrong before it.
                    run.Graph.Failures = 0;
                }
                return;
            }
            GraphStmt s = at.Statements[at.At++];
            switch (s)
            {
                case CallStmt c:
                {
                    var args = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, GraphExpr> arg in c.Args)
                    {
                        args[arg.Key] = Value(arg.Value, run);
                    }
                    GraphCallResult result = _world.Call(run.Graph, c.Operation, args);
                    if (result.Ok)
                    {
                        if (result.TakenBackBy != null)
                        {
                            run.Graph.TakeBacks.Add(result.TakenBackBy);
                        }
                        if (c.As != null)
                        {
                            run.Locals[c.As] = result.Value;
                        }
                    }
                    else if (c.OnError != null)
                    {
                        run.Locals["error"] = result.Error;
                        run.Stack.Add(new GraphRun.Position { Statements = c.OnError });
                    }
                    else
                    {
                        throw new GraphFailure($"{c.Id}: {c.Operation}: {result.Error}");
                    }
                    return;
                }
                case SetStmt set:
                    run.Graph.Variables[set.Name] = Value(set.To, run);
                    return;
                case IfStmt test:
                    run.Stack.Add(new GraphRun.Position { Statements = GraphValues.Truthy(Value(test.Test, run)) ? test.Then : test.Else });
                    return;
                case WaitStmt wait:
                    // 0 is "the next frame": the clock does not move inside one.
                    run.Wake = _world.Now + Math.Max(wait.Seconds, 1e-6);
                    return;
                case RepeatStmt repeat:
                    run.Stack.Add(new GraphRun.Position { Statements = repeat.Do, Loop = new GraphRun.Loop { Left = repeat.Times - 1 } });
                    return;
                case WhileStmt loop:
                    if (GraphValues.Truthy(Value(loop.Test, run)))
                    {
                        run.Stack.Add(new GraphRun.Position { Statements = loop.Do, Loop = new GraphRun.Loop { While = loop, Left = loop.Max - 1 } });
                    }
                    return;
                case EachStmt each:
                {
                    object items = Value(each.Items, run);
                    var list = items as IList;
                    if (list == null)
                    {
                        throw new GraphFailure($"{each.Id}: each needs a list");
                    }
                    if (list.Count > each.Max)
                    {
                        throw new GraphFailure($"{each.Id}: {list.Count} items, more than max {each.Max}");
                    }
                    if (list.Count > 0)
                    {
                        run.Locals[each.As] = list[0];
                        run.Stack.Add(new GraphRun.Position { Statements = each.Do, Loop = new GraphRun.Loop { Items = list, ItemAt = 0, As = each.As } });
                    }
                    return;
                }
                case LogStmt line:
                    Write(run.Graph, line.Level, GraphValues.Text(Value(line.Text, run)));
                    return;
                case StopStmt _:
                    run.Stack.Clear();
                    run.Done = true;
                    run.Graph.Failures = 0;
                    return;
            }
        }

        private bool LoopAgain(GraphRun run, GraphRun.Position at)
        {
            GraphRun.Loop loop = at.Loop;
            if (loop == null)
            {
                return false;
            }
            if (loop.Items != null)
            {
                loop.ItemAt++;
                if (loop.ItemAt >= loop.Items.Count)
                {
                    return false;
                }
                run.Locals[loop.As] = loop.Items[loop.ItemAt];
            }
            else if (loop.While != null)
            {
                if (!GraphValues.Truthy(Value(loop.While.Test, run)))
                {
                    return false;
                }
                if (loop.Left <= 0)
                {
                    // Still true after its max: a loop that never ends is a
                    // mistake, not something to carry on with quietly.
                    throw new GraphFailure($"{loop.While.Id}: still true after max {loop.While.Max} rounds");
                }
                loop.Left--;
            }
            else
            {
                if (loop.Left <= 0)
                {
                    return false;
                }
                loop.Left--;
            }
            at.At = 0;
            return true;
        }

        // ---- expressions ------------------------------------------------------

        private object Value(GraphExpr e, GraphRun run)
        {
            switch (e.Kind)
            {
                case ExprKind.Literal:
                    return e.Literal;
                case ExprKind.Var:
                {
                    object local;
                    if (run.Locals.TryGetValue(e.Name, out local))
                    {
                        return local;
                    }
                    object value;
                    return run.Graph.Variables.TryGetValue(e.Name, out value) ? value : null;
                }
                case ExprKind.Event:
                {
                    object value;
                    return run.Event.TryGetValue(e.Name, out value) ? value : null;
                }
                case ExprKind.Get:
                {
                    object value = Value(e.Args[0], run);
                    foreach (object key in e.Keys)
                    {
                        value = GraphValues.Get(value, key);
                        if (value == null)
                        {
                            return null;
                        }
                    }
                    return value;
                }
                case ExprKind.And:
                    foreach (GraphExpr a in e.Args)
                    {
                        if (!GraphValues.Truthy(Value(a, run))) return false;
                    }
                    return true;
                case ExprKind.Or:
                    foreach (GraphExpr a in e.Args)
                    {
                        if (GraphValues.Truthy(Value(a, run))) return true;
                    }
                    return false;
                case ExprKind.Join:
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (GraphExpr a in e.Args)
                    {
                        sb.Append(GraphValues.Text(Value(a, run)));
                    }
                    return sb.ToString();
                }
                case ExprKind.Not:
                    return !GraphValues.Truthy(Value(e.Args[0], run));
                case ExprKind.Length:
                    return (double)GraphValues.Length(Value(e.Args[0], run));
            }
            object left = Value(e.Args[0], run);
            object right = Value(e.Args[1], run);
            switch (e.Kind)
            {
                case ExprKind.Eq: return GraphValues.Same(left, right);
                case ExprKind.Ne: return !GraphValues.Same(left, right);
                case ExprKind.Lt: return GraphValues.Compare(left, right) < 0;
                case ExprKind.Le: return GraphValues.Compare(left, right) <= 0;
                case ExprKind.Gt: return GraphValues.Compare(left, right) > 0;
                case ExprKind.Ge: return GraphValues.Compare(left, right) >= 0;
                case ExprKind.Contains: return GraphValues.Contains(left, right);
            }
            if (!GraphValues.IsNumber(left) || !GraphValues.IsNumber(right))
            {
                throw new GraphFailure($"{Word(e.Kind)} needs numbers, got \"{GraphValues.Text(left)}\" and \"{GraphValues.Text(right)}\"");
            }
            double a2 = GraphValues.Number(left);
            double b2 = GraphValues.Number(right);
            switch (e.Kind)
            {
                case ExprKind.Add: return a2 + b2;
                case ExprKind.Sub: return a2 - b2;
                case ExprKind.Mul: return a2 * b2;
                default:
                    if (b2 == 0)
                    {
                        throw new GraphFailure("division by zero");
                    }
                    return a2 / b2;
            }
        }

        private static string Word(ExprKind kind) => kind.ToString().ToLowerInvariant();

        // ---- failures, stopping and putting back ------------------------------

        private void Fail(GraphRun run, string why)
        {
            run.Done = true;
            Graph graph = run.Graph;
            graph.Failures++;
            _world.Write(GraphLevel.Error, $"{graph.Where} {run.Handler.Id}: {why}");
            if (graph.Failures >= GraphLimits.FailuresBeforeOff)
            {
                Stop(graph, $"it failed {graph.Failures} times in a row");
            }
        }

        /// <summary>Switches a graph off for the session and puts back what it changed.</summary>
        internal void Stop(Graph graph, string why)
        {
            graph.Stopped = true;
            graph.StoppedWhy = why;
            foreach (GraphRun run in graph.Runs)
            {
                run.Done = true;
            }
            graph.Runs.Clear();
            int back = TakeBackAll(graph);
            _world.Write(GraphLevel.Warning, $"{graph.Where} is switched off for this session because {why}{(back > 0 ? $"; {back} change(s) put back" : "")}.");
            _world.Unavailable(graph, why);
        }

        /// <summary>
        /// Puts back what a graph changed, newest first: a later write may have
        /// been made on top of an earlier one, or on top of another graph's.
        /// </summary>
        internal int TakeBackAll(Graph graph)
        {
            int back = 0;
            for (int i = graph.TakeBacks.Count - 1; i >= 0; i--)
            {
                if (graph.TakeBacks[i].Run())
                {
                    back++;
                }
            }
            graph.TakeBacks.Clear();
            return back;
        }

        /// <summary>
        /// Looks again at the operations each graph uses. A library that is
        /// reloaded or unloaded takes its operations with it; the graphs that
        /// need them wait instead of failing, and go on when they are back.
        /// </summary>
        internal void CheckOperations()
        {
            foreach (Graph graph in Graphs)
            {
                if (!graph.Ok || graph.Stopped)
                {
                    continue;
                }
                string missing = null;
                foreach (string name in graph.Uses.Keys)
                {
                    GraphOperation op = _world.FindOperation(name);
                    if (op == null || !op.ForGraphs)
                    {
                        missing = name;
                        break;
                    }
                }
                if (missing != null && graph.Waiting == null)
                {
                    graph.Waiting = missing;
                    // Not a failure: its library went, it did nothing wrong.
                    foreach (GraphRun run in graph.Runs)
                    {
                        run.Done = true;
                    }
                    graph.Runs.Clear();
                    _world.Write(GraphLevel.Info, $"{graph.Where} waits: {missing} is gone with its library.");
                }
                else if (missing == null && graph.Waiting != null)
                {
                    _world.Write(GraphLevel.Info, $"{graph.Where} goes on: {graph.Waiting} is back.");
                    graph.Waiting = null;
                }
            }
        }

        // A graph writes at most 20 lines a second; the rest are counted and
        // said once, so a log statement inside a loop cannot fill the log file.
        private void Write(Graph graph, GraphLevel level, string line)
        {
            double now = _world.Now;
            if (now - graph.LogWindow >= 1)
            {
                if (graph.LogsLeftOut > 0)
                {
                    _world.Write(GraphLevel.Warning, $"{graph.Where}: {graph.LogsLeftOut} line(s) were left out (at most {GraphLimits.LogLinesPerSecond} a second).");
                    graph.LogsLeftOut = 0;
                }
                graph.LogWindow = now;
                graph.LogLines = 0;
            }
            if (graph.LogLines >= GraphLimits.LogLinesPerSecond)
            {
                graph.LogsLeftOut++;
                return;
            }
            graph.LogLines++;
            _world.Write(level, $"{graph.Where}: {line}");
        }
    }
}
