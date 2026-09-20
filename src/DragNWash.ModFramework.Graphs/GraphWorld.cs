using System;
using System.Collections.Generic;

namespace DragNWash.ModFramework.Graphs
{
    // Everything the checker and the runner know of the world outside them: the
    // operations and events of the registry, the clock, the log. In the game
    // GraphRegistry answers it from Operations; the test harness in
    // tools/graphs-test answers it with stand-ins, so the checker and the
    // interpreter can be run without Unity, BepInEx or the game.
    internal interface IGraphWorld
    {
        // Null when nothing of that name is registered now.
        GraphOperation FindOperation(string name);

        GraphEventKind FindEvent(string name);

        // Every operation and event registered now, by name: for the message
        // that lists the events, and for telling a library that is not
        // installed from a name that is misspelt.
        string[] OperationNames { get; }

        string[] EventNames { get; }

        // Null when the key name is one a graph may answer, else why not.
        string KeyProblem(string key);

        // Seconds, real time: the game's pause and time scale do not stretch it.
        double Now { get; }

        GraphCallResult Call(Graph graph, string name, Dictionary<string, object> args);

        void Write(GraphLevel level, string line);

        // A graph switched off for the session, for the Mods screen.
        void Unavailable(Graph graph, string reason);
    }

    /// <summary>How loud a line from a graph is.</summary>
    internal enum GraphLevel
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    // What a graph needs to know of an operation. The registry's own Operation
    // carries more (descriptions, the function); this is the part the checker
    // and the disclosure on the Mods screen use.
    internal sealed class GraphOperation
    {
        internal string Name;
        internal bool Changes;
        internal bool Lasting;
        internal bool ForGraphs;
        internal IList<GraphParameter> Parameters = new List<GraphParameter>();
    }

    internal sealed class GraphParameter
    {
        internal string Name;
        internal GraphValueType Type;
        internal bool Required;
        internal string[] Choices;
    }

    internal enum GraphValueType
    {
        Text,
        Number,
        Boolean,
    }

    internal sealed class GraphEventKind
    {
        internal string Name;
        internal string[] Values = new string[0];
    }

    internal sealed class GraphCallResult
    {
        internal bool Ok;
        internal object Value;
        internal string Error;
        // Set when the write said how to put itself back; the graph keeps it.
        internal GraphTakeBack TakenBackBy;
    }

    // One change a graph made and how to put it back. The registry hands over an
    // OperationTakeBack; a graph only ever needs its name and its Run, so that is
    // all that crosses into the runner.
    internal sealed class GraphTakeBack
    {
        internal string Operation;
        internal string Label;
        internal Func<bool> Run;
    }
}
