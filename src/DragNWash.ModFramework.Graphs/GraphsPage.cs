using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Graphs
{
    // The Graphs page of a mod on the Mods screen: what each of its graphs
    // answers, what it uses, how it is going, and a button that stops one for
    // this session.
    //
    // The page is built from what GameGraphs reports, so the console command
    // and this page always say the same thing. Each graph is a card, with the
    // Mods screen's colours and its button (ModsScreenLook, see Look below);
    // the rest is made with TextMeshPro and UnityEngine.UI directly.
    internal static class GraphsPage
    {
        private const float Size = 20f;

        internal static void Build(RectTransform panel, string guid)
        {
            Look look = Look.Get();
            RectTransform content = ScrollArea(panel, look);
            List<GameGraphs.GraphReport> graphs;
            try
            {
                graphs = GameGraphs.Loaded.Where(r => r.ModGuid == guid).ToList();
            }
            catch (Exception ex)
            {
                // The Mods screen belongs to the player and to every other mod
                // on it: this page saying nothing is better than the screen
                // failing to draw.
                GraphsPlugin.Log.LogError($"[graphs] The Mods screen's page could not be built: {ex}");
                Line(content, "The graphs could not be read: " + ex.Message, Size, FontStyles.Italic, look.Error, 0f);
                return;
            }

            if (graphs.Count == 0)
            {
                Line(content, "This mod has no graphs the library could read.", Size, FontStyles.Italic, look.Muted, 0f);
                return;
            }

            // A card for each graph, like a setting's row on the Settings tab.
            foreach (GameGraphs.GraphReport graph in graphs)
            {
                RectTransform card = look.Card(content);
                Line(card, graph.File, Size * 1.1f, FontStyles.Bold, look.Text, 0f);
                if (!string.IsNullOrEmpty(graph.Name) && graph.Name != graph.File)
                {
                    Line(card, graph.Name, Size, FontStyles.Normal, look.Muted, 0f);
                }

                Line(card, graph.State, Size, FontStyles.Normal,
                    graph.Problems.Count > 0 ? look.Error : graph.State == "runs" ? look.Accent : look.Muted, 0f);

                foreach (string problem in graph.Problems)
                {
                    Line(card, "• " + problem, Size * 0.9f, FontStyles.Normal, look.Error, 0f);
                }

                if (graph.Events.Count > 0)
                {
                    Pair(card, look, "Answers", string.Join(", ", graph.Events.ToArray()), look.Muted);
                }
                if (graph.Reads.Count > 0)
                {
                    Pair(card, look, "Reads", string.Join(", ", graph.Reads.ToArray()), look.Muted);
                }
                if (graph.Changes.Count > 0)
                {
                    // What a graph changes is the thing a player most wants to
                    // see before switching the mod on, so it is not dimmed.
                    Pair(card, look, "Changes", string.Join(", ", graph.Changes.ToArray()), look.Text);
                }
                if (graph.Needs.Count > 0)
                {
                    Pair(card, look, "Needs", string.Join(", ", graph.Needs.ToArray()), look.Error);
                }
                foreach (string share in graph.Shares)
                {
                    Pair(card, look, "Key shared", share, look.Text);
                }
                foreach (string clash in graph.Clashes)
                {
                    // Why an edit may seem to do nothing: somebody else writes
                    // the same value, and the later write is the one that stands.
                    Pair(card, look, "Also changed", clash.Replace(" - also changed by ", ": "), look.Text);
                }
                if (graph.Started > 0)
                {
                    Pair(card, look, "Runs", $"{graph.Running} going, {graph.Started} started this session" +
                                             (graph.Failures > 0 ? $", {graph.Failures} failure(s) in a row" : ""), look.Muted);
                }

                if (graph.Problems.Count == 0)
                {
                    string file = graph.File;
                    StopButton(card, look, guid, file);
                }
            }

            // Under the cards, its text in line with theirs.
            Spacer(content, 4f);
            Line(content, "A graph may only call the operations the libraries registered. Stopping one puts back what it changed; it starts again the next time the game does, or with \"graphs reload\" in the console.",
                Size * 0.85f, FontStyles.Italic, look.Muted, Look.CardPadding);
        }

        private static void StopButton(RectTransform card, Look look, string guid, string file)
        {
            var row = new GameObject("StopRow", typeof(RectTransform));
            var rowRect = (RectTransform)row.transform;
            rowRect.SetParent(card, false);
            HorizontalLayoutGroup line = row.AddComponent<HorizontalLayoutGroup>();
            line.padding = new RectOffset(0, 0, 4, 0);
            // The layout group has to control the width for the button's
            // LayoutElement to mean anything; without it the button keeps a new
            // RectTransform's 100 px and the label is cut to "Stop for ...".
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = false;

            Button press = null;
            TMP_Text label = null;
            press = look.Button(rowRect, "Stop for this session", () =>
            {
                string said = GameGraphs.Stop(guid, file);
                label.text = said.Contains("stopped") ? "Stopped" : "Could not stop it";
                // Once used it says "Stopped" and can't be pressed again.
                press.interactable = false;
            });
            press.name = "Stop";
            label = press.GetComponentInChildren<TMP_Text>();
        }

        // ---- the pieces --------------------------------------------------------

        // What a graph has, under a small bold label.
        private static void Pair(RectTransform card, Look look, string label, string value, Color color)
        {
            var group = new GameObject("Pair", typeof(RectTransform));
            var groupRect = (RectTransform)group.transform;
            groupRect.SetParent(card, false);
            VerticalLayoutGroup column = group.AddComponent<VerticalLayoutGroup>();
            column.spacing = 2f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            Line(groupRect, label, Size * 0.9f, FontStyles.Bold, look.Muted, 0f);
            Line(groupRect, value, Size * 0.9f, FontStyles.Normal, color, 0f);
        }

        // A scrolling column, so a mod with many graphs still fits the panel.
        private static RectTransform ScrollArea(RectTransform panel, Look look)
        {
            var viewport = new GameObject("GraphsViewport", typeof(RectTransform), typeof(RectMask2D));
            var viewRect = (RectTransform)viewport.transform;
            viewRect.SetParent(panel, false);
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;
            // Catches the wheel over empty space. Clear on the Mods screen's
            // panel; on an older core's see-through band, the page's ground.
            Image ground = viewport.AddComponent<Image>();
            ground.color = look.Ground;

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
            layout.spacing = 10f;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.viewport = viewRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            // A notch of the wheel moves about a row, as on the rest of the
            // Mods screen (the game's input gives 6 a notch).
            scroll.scrollSensitivity = 14f;
            return contentRect;
        }

        // A line of text that wraps, `indent` in from the left and the right.
        private static void Line(RectTransform content, string text, float size, FontStyles style, Color color, float indent)
        {
            var row = new GameObject("Line", typeof(RectTransform));
            ((RectTransform)row.transform).SetParent(content, false);
            HorizontalLayoutGroup pad = row.AddComponent<HorizontalLayoutGroup>();
            pad.padding = new RectOffset((int)indent, (int)indent, 0, 0);
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

        // The colours and pieces the page is built with: the Mods screen's
        // own (ModsScreenLook, core 1.5.0 and later), so the page sits on the
        // screen's see-through panel like its other tabs do. An older core
        // has no ModsScreenLook and draws the page straight over the game's
        // see-through band, so there the page keeps copies of the Tool
        // window's colours on a dark ground of its own, as it did before.
        // ModsScreenLook is only touched in the NoInlining methods, so its
        // absence is caught in Get instead of taking the page down.
        private sealed class Look
        {
            // A card's inner padding on the left (ModsScreenLook.Card), for
            // text under the cards to line up with the text in them.
            internal const float CardPadding = 20f;

            internal Color Text;
            internal Color Muted;
            internal Color Accent;
            internal Color Error;
            internal Color Ground;
            private bool _screen;

            internal static Look Get()
            {
                try
                {
                    return FromScreen();
                }
                catch (Exception ex) when (ex is TypeLoadException || ex is MissingMemberException)
                {
                    return new Look
                    {
                        Text = new Color(0.91f, 0.94f, 0.97f),
                        Muted = new Color(0.60f, 0.66f, 0.73f),
                        Accent = new Color(0.32f, 0.78f, 0.72f),
                        Error = new Color(0.96f, 0.45f, 0.40f),
                        Ground = new Color(0.09f, 0.11f, 0.15f),
                    };
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Look FromScreen()
            {
                return new Look
                {
                    Text = ModsScreenLook.Text,
                    Muted = ModsScreenLook.Muted,
                    Accent = ModsScreenLook.Accent,
                    Error = ModsScreenLook.Error,
                    Ground = new Color(0f, 0f, 0f, 0f),
                    _screen = true,
                };
            }

            internal RectTransform Card(RectTransform parent)
            {
                return _screen ? ScreenCard(parent) : OwnCard(parent);
            }

            internal Button Button(RectTransform parent, string text, UnityAction onClick)
            {
                return _screen ? ScreenButton(parent, text, onClick) : OwnButton(parent, text, onClick);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static RectTransform ScreenCard(RectTransform parent)
            {
                return ModsScreenLook.Card(parent);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Button ScreenButton(RectTransform parent, string text, UnityAction onClick)
            {
                return ModsScreenLook.Button(parent, text, onClick);
            }

            // On the page's own ground: a column with the card's padding.
            private static RectTransform OwnCard(RectTransform parent)
            {
                var card = new GameObject("Card", typeof(RectTransform));
                var rect = (RectTransform)card.transform;
                rect.SetParent(parent, false);
                VerticalLayoutGroup column = card.AddComponent<VerticalLayoutGroup>();
                column.padding = new RectOffset((int)CardPadding, 14, 12, 12);
                column.spacing = 8f;
                column.childControlWidth = true;
                column.childControlHeight = true;
                column.childForceExpandWidth = true;
                column.childForceExpandHeight = false;
                return rect;
            }

            // A plain button in the Tool window's colours.
            private Button OwnButton(RectTransform parent, string text, UnityAction onClick)
            {
                var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
                ((RectTransform)go.transform).SetParent(parent, false);
                Button button = go.GetComponent<Button>();
                button.targetGraphic = go.GetComponent<Image>();
                ColorBlock colors = button.colors;
                colors.normalColor = new Color(0.165f, 0.2f, 0.26f);
                colors.highlightedColor = new Color(0.24f, 0.29f, 0.37f);
                colors.selectedColor = colors.highlightedColor;
                colors.pressedColor = new Color(0.055f, 0.07f, 0.10f);
                colors.disabledColor = colors.normalColor;
                // At once: a fade would start from the new Image's white.
                colors.fadeDuration = 0f;
                button.colors = colors;
                TMP_Text label = GraphsPage.Text(go.transform, text, Size, FontStyles.Bold, Text);
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                LayoutElement size = go.AddComponent<LayoutElement>();
                size.minWidth = size.preferredWidth = Mathf.Ceil(label.preferredWidth) + 32f;
                size.minHeight = size.preferredHeight = 44f;
                button.onClick.AddListener(onClick);
                return button;
            }
        }
    }
}
