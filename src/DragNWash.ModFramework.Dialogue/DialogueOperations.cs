using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Yarn.Unity;

namespace DragNWash.ModFramework.Dialogue
{
    // The Dialogue library's operations (docs/API_PLAN.md, stage 1): all
    // read. The library remembers the last lines shown this session.
    internal static class DialogueOperations
    {
        private const int Kept = 100;
        private static readonly List<Dictionary<string, object>> Recent = new List<Dictionary<string, object>>();

        internal static void Register()
        {
            string g = GameDialogue.Guid;
            GameDialogue.LineShowing += line => Remember(line, "line");
            GameDialogue.OptionShowing += line => Remember(line, "option");
            RegisterEvents(g);

            Operations.Register(g, "dialogue.current", "The conversation now: whether one is running, the node, whether options are on screen, and the last line shown.", OperationKind.Read,
                "{ running, node, options_showing, last, hooks: { lines, options } } (hooks: whether this game build lets the library see lines and options at all)", args =>
                {
                    bool running = UnityEngine.Object.FindObjectsByType<DialogueRunner>(FindObjectsSortMode.None).Any(r => r != null && r.IsDialogueRunning);
                    bool options = UnityEngine.Object.FindObjectsByType<OptionItem>(FindObjectsSortMode.None).Any(o => o != null && o.isActiveAndEnabled && o.Option != null);
                    return new Dictionary<string, object>
                    {
                        ["running"] = running,
                        ["node"] = running ? GameDialogue.CurrentNode : null,
                        ["options_showing"] = options,
                        ["last"] = Recent.LastOrDefault(),
                        ["hooks"] = new Dictionary<string, object> { ["lines"] = GameDialogue.LinesAvailable, ["options"] = GameDialogue.OptionsAvailable },
                    };
                });

            Operations.Register(g, "dialogue.recent", "The last lines and options shown this session, newest last, optionally only those that contain a text.", OperationKind.Read,
                "a list of { kind, line_id, node, speaker, speaker_from, text } (speaker: named in the script, else guessed from the node, and Kobold for options)", args =>
                {
                    string text = args.String("text");
                    int max = Math.Max(1, Math.Min(Kept, args.Int("max", 20)));
                    List<Dictionary<string, object>> found = Recent
                        .Where(r => text == null || ((string)r["text"] ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    return found.Skip(Math.Max(0, found.Count - max)).Cast<object>().ToList();
                },
                Operations.Parameter("text", OperationType.String, "Only lines that contain this."),
                Operations.Parameter("max", OperationType.Number, $"At most this many (1 to {Kept}; 20 when left out)."));
        }

        // The library's events in the registry, raised beside its own: the
        // library owns them, because it owns the hooks they come from, and what
        // answers them by name (a graph) never needs to know that.
        private static void RegisterEvents(string g)
        {
            Operations.RegisterEvent(g, "dialogue.node.started", "A conversation node starts.",
                Operations.Parameter("node", OperationType.String, "The node's name."));
            Operations.RegisterEvent(g, "dialogue.line.showing", "A line is about to be shown.",
                Operations.Parameter("line_id", OperationType.String, "The Yarn line ID, e.g. line:6046bedf."),
                Operations.Parameter("node", OperationType.String, "The node it is in."),
                Operations.Parameter("speaker", OperationType.String, "Who says it: named in the script, else guessed from the node."),
                Operations.Parameter("text", OperationType.String, "The line as it is shown."));
            Operations.RegisterEvent(g, "dialogue.option.showing", "An option is about to be shown.",
                Operations.Parameter("line_id", OperationType.String, "The Yarn line ID."),
                Operations.Parameter("node", OperationType.String, "The node it is in."),
                Operations.Parameter("text", OperationType.String, "The option as it is shown."));
            GameDialogue.NodeStarted += node => Operations.Raise("dialogue.node.started", new Dictionary<string, object> { ["node"] = node });
            GameDialogue.LineShowing += line => Operations.Raise("dialogue.line.showing", Values(line, true));
            GameDialogue.OptionShowing += line => Operations.Raise("dialogue.option.showing", Values(line, false));
        }

        private static Dictionary<string, object> Values(DialogueLine line, bool withSpeaker)
        {
            var values = new Dictionary<string, object>
            {
                ["line_id"] = line?.LineId,
                ["node"] = line?.Node,
                ["text"] = line?.Text,
            };
            if (withSpeaker) values["speaker"] = line?.SpeakerGuess;
            return values;
        }

        private static void Remember(DialogueLine line, string kind)
        {
            if (line == null) return;
            Recent.Add(new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["line_id"] = line.LineId,
                ["node"] = line.Node,
                ["speaker"] = line.SpeakerGuess,
                ["speaker_from"] = line.SpeakerFrom,
                ["text"] = line.Text,
            });
            if (Recent.Count > Kept) Recent.RemoveAt(0);
        }
    }
}
