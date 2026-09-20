using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Graphs
{
    // The Graphs page of a mod on the Mods screen: what each of its graphs
    // answers, what it uses, how it is going, and a button that stops one for
    // this session.
    //
    // The page is built from what GameGraphs reports, so the console command
    // and this page always say the same thing. The core's own page helpers are
    // internal to it, so the few pieces used here are made with TextMeshPro and
    // UnityEngine.UI directly.
    internal static class GraphsPage
    {
        private const float Size = 20f;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.65f);
        private static readonly Color Bad = new Color(1f, 0.55f, 0.45f, 1f);
        private static readonly Color Good = new Color(0.65f, 0.95f, 0.7f, 1f);

        internal static void Build(RectTransform panel, string guid)
        {
            RectTransform content = ScrollArea(panel);
            List<GameGraphs.GraphReport> graphs = GameGraphs.Loaded.Where(r => r.ModGuid == guid).ToList();

            if (graphs.Count == 0)
            {
                Line(content, "This mod has no graphs the library could read.", Size, FontStyles.Italic, Dim, 0f);
                return;
            }

            foreach (GameGraphs.GraphReport graph in graphs)
            {
                Line(content, graph.File, Size * 1.1f, FontStyles.Bold, Color.white, 0f);
                if (!string.IsNullOrEmpty(graph.Name) && graph.Name != graph.File)
                {
                    Line(content, graph.Name, Size, FontStyles.Normal, Dim, 24f);
                }

                Line(content, graph.State, Size, FontStyles.Normal,
                    graph.Problems.Count > 0 ? Bad : graph.State == "runs" ? Good : Dim, 24f);

                foreach (string problem in graph.Problems)
                {
                    Line(content, "• " + problem, Size * 0.9f, FontStyles.Normal, Bad, 48f);
                }

                if (graph.Events.Count > 0)
                {
                    Pair(content, "Answers", string.Join(", ", graph.Events.ToArray()));
                }
                if (graph.Reads.Count > 0)
                {
                    Pair(content, "Reads", string.Join(", ", graph.Reads.ToArray()));
                }
                if (graph.Changes.Count > 0)
                {
                    // What a graph changes is the thing a player most wants to
                    // see before switching the mod on, so it is not dimmed.
                    Pair(content, "Changes", string.Join(", ", graph.Changes.ToArray()), Color.white);
                }
                if (graph.Needs.Count > 0)
                {
                    Pair(content, "Needs", string.Join(", ", graph.Needs.ToArray()), Bad);
                }
                foreach (string share in graph.Shares)
                {
                    Pair(content, "Key shared", share, Color.white);
                }
                foreach (string clash in graph.Clashes)
                {
                    // Why an edit may seem to do nothing: somebody else writes
                    // the same value, and the later write is the one that stands.
                    Pair(content, "Also changed", clash.Replace(" - also changed by ", ": "), Color.white);
                }
                if (graph.Started > 0)
                {
                    Pair(content, "Runs", $"{graph.Running} going, {graph.Started} started this session" +
                                          (graph.Failures > 0 ? $", {graph.Failures} failure(s) in a row" : ""));
                }

                if (graph.Problems.Count == 0)
                {
                    string file = graph.File;
                    StopButton(content, file);
                }
                Spacer(content, Size * 0.8f);
            }

            Line(content, "A graph may only call the operations the libraries registered. Stopping one puts back what it changed; it starts again the next time the game does, or with \"graphs reload\" in the console.",
                Size * 0.85f, FontStyles.Italic, Dim, 0f);
        }

        private static void StopButton(RectTransform content, string file)
        {
            var row = new GameObject("StopRow", typeof(RectTransform));
            var rowRect = (RectTransform)row.transform;
            rowRect.SetParent(content, false);
            HorizontalLayoutGroup pad = row.AddComponent<HorizontalLayoutGroup>();
            pad.padding = new RectOffset(24, 0, 4, 4);
            pad.childControlHeight = true;
            // The layout group has to control the width for the button's
            // LayoutElement to mean anything; without it the button keeps a new
            // RectTransform's 100 px and the label is cut to "Stop for ...".
            pad.childControlWidth = true;
            pad.childForceExpandWidth = false;

            var button = new GameObject("Stop", typeof(RectTransform), typeof(Image), typeof(Button));
            ((RectTransform)button.transform).SetParent(rowRect, false);
            button.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            LayoutElement size = button.AddComponent<LayoutElement>();
            size.minWidth = 300f;
            size.preferredWidth = 300f;
            size.minHeight = Size * 2f;

            TMP_Text label = Text(button.transform, "Stop for this session", Size, FontStyles.Normal, Color.white);
            label.alignment = TextAlignmentOptions.Center;
            // On one line: wrapped, the label grew past the button and printed
            // over the line above it.
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Button press = button.GetComponent<Button>();
            press.onClick.AddListener(() =>
            {
                string said = GameGraphs.Stop(file);
                label.text = said.Contains("stopped") ? "Stopped" : "Could not stop it";
                press.interactable = false;
            });
        }

        // ---- the pieces --------------------------------------------------------

        private static void Pair(RectTransform content, string label, string value)
        {
            Pair(content, label, value, Dim);
        }

        private static void Pair(RectTransform content, string label, string value, Color color)
        {
            Line(content, label, Size * 0.9f, FontStyles.Bold, Dim, 24f);
            Line(content, value, Size * 0.9f, FontStyles.Normal, color, 48f);
        }

        // A scrolling column, so a mod with many graphs still fits the panel.
        private static RectTransform ScrollArea(RectTransform panel)
        {
            var viewport = new GameObject("GraphsViewport", typeof(RectTransform), typeof(RectMask2D));
            var viewRect = (RectTransform)viewport.transform;
            viewRect.SetParent(panel, false);
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;
            // Something to catch the wheel over empty space.
            Image catcher = viewport.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);

            var content = new GameObject("GraphsContent", typeof(RectTransform));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(viewRect, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 2f;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.viewport = viewRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return contentRect;
        }

        private static void Line(RectTransform content, string text, float size, FontStyles style, Color color, float indent)
        {
            var row = new GameObject("Line", typeof(RectTransform));
            ((RectTransform)row.transform).SetParent(content, false);
            HorizontalLayoutGroup pad = row.AddComponent<HorizontalLayoutGroup>();
            pad.padding = new RectOffset((int)indent, 0, 0, 0);
            pad.childControlHeight = true;
            pad.childControlWidth = true;
            pad.childForceExpandWidth = true;

            TMP_Text label = Text(row.transform, text, size, style, color);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        private static TMP_Text Text(Transform parent, string text, float size, FontStyles style, Color color)
        {
            var holder = new GameObject("Text", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            TextMeshProUGUI label = holder.AddComponent<TextMeshProUGUI>();
            label.text = text ?? "";
            label.enableAutoSizing = false;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            return label;
        }

        private static void Spacer(RectTransform content, float height)
        {
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            ((RectTransform)spacer.transform).SetParent(content, false);
            spacer.GetComponent<LayoutElement>().minHeight = height;
        }
    }
}
