using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DragNWash.ModFramework.Graphs
{
    // The world the checker and the runner work against, answered from the
    // registry and from Unity. Everything a graph can reach passes through here:
    // it is the only place in the library that knows what an Operation is.
    internal sealed class GraphRegistry : IGraphWorld
    {
        // The framework's own key: a graph that took it would take the F1 window
        // away from the player, so it is refused when the file is read.
        private static readonly KeyCode[] Framework = { KeyCode.F1 };

        public GraphOperation FindOperation(string name)
        {
            Operation op = Operations.Find(name);
            if (op == null)
            {
                return null;
            }
            return new GraphOperation
            {
                Name = op.Name,
                Changes = op.Kind == OperationKind.Write,
                Lasting = op.Lasting,
                ForGraphs = (op.Audience & OperationAudience.Graphs) != 0,
                Parameters = op.Parameters.Select(p => new GraphParameter
                {
                    Name = p.Name,
                    Type = p.Type == OperationType.Number ? GraphValueType.Number : p.Type == OperationType.Boolean ? GraphValueType.Boolean : GraphValueType.Text,
                    Required = p.Required,
                    Choices = p.Choices,
                }).ToList(),
            };
        }

        public GraphEventKind FindEvent(string name)
        {
            OperationEvent e = Operations.FindEvent(name);
            return e == null ? null : new GraphEventKind { Name = e.Name, Values = e.Values.Select(v => v.Name).ToArray() };
        }

        public string[] OperationNames => Operations.All.Select(o => o.Name).ToArray();

        public string[] EventNames => Operations.AllEvents.Select(e => e.Name).ToArray();

        public string KeyProblem(string key)
        {
            KeyCode code;
            if (!Enum.TryParse(key, true, out code) || code == KeyCode.None)
            {
                return $"\"{key}\" is not a key name; they are Unity's, such as F6, G or LeftBracket";
            }
            if (code >= KeyCode.Mouse0 || (code >= KeyCode.JoystickButton0 && code <= KeyCode.Joystick8Button19))
            {
                return $"{key} is not a keyboard key; key.pressed answers the keyboard";
            }
            if (Array.IndexOf(Framework, code) >= 0)
            {
                return $"{key} is the framework's own key (the tool window) and cannot be answered";
            }
            return null;
        }

        // Real time: the game's pause and time scale do not stretch a graph's wait.
        public double Now => Time.unscaledTime;

        public GraphCallResult Call(Graph graph, string name, Dictionary<string, object> args)
        {
            // The caller is in every line the registry logs, and in the
            // Inspector's History for a write, so a change can be traced to the
            // graph that made it.
            string caller = $"graph:{graph.ModGuid}/{graph.File}";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            // A graph keeps the result in memory, so the JSON size check the
            // Bridge needs is skipped: it costs more than most calls, and an
            // "each" that is too long already fails at its max.
            OperationResult result = Operations.CallNow(name, args, caller, OperationAudience.Graphs, measureResult: false);
            double ms = watch.Elapsed.TotalMilliseconds;
            if (ms >= GraphLimits.SlowCallMs)
            {
                // The budget cannot cut a call in half, so a slow one shows in
                // the frame; said the way a slow GameEvents handler is.
                GraphsPlugin.Log.LogDebug($"[graphs] {graph.Where}: {name} took {ms:F1} ms.");
            }
            return new GraphCallResult
            {
                Ok = result.Ok,
                Value = result.Value,
                Error = result.Error,
                TakenBackBy = result.TakenBackBy == null ? null : new GraphTakeBack
                {
                    Operation = result.TakenBackBy.Operation,
                    Label = result.TakenBackBy.Label,
                    Run = result.TakenBackBy.Run,
                },
            };
        }

        public void Write(GraphLevel level, string line)
        {
            line = "[graphs] " + line;
            switch (level)
            {
                case GraphLevel.Debug: GraphsPlugin.Log.LogDebug(line); break;
                case GraphLevel.Warning: GraphsPlugin.Log.LogWarning(line); break;
                case GraphLevel.Error: GraphsPlugin.Log.LogError(line); break;
                default: GraphsPlugin.Log.LogInfo(line); break;
            }
        }

        public void Unavailable(Graph graph, string reason)
        {
            // Under the graph's own mod, so the Mods screen marks that mod, not
            // the library: it is the mod's graph that stopped.
            GameHooks.Unavailable(graph.ModGuid, "Graph " + graph.File, reason);
        }
    }
}
