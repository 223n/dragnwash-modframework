using System;
using System.Collections.Generic;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using DragNWash.ModFramework.ToolWindow;
using UnityEngine;

namespace DragNWash.ModFramework.Assets
{
    // The "Assets" tab of the Tool window: what textures are loaded, which
    // replacements mods shipped and where they clashed, and a button to apply
    // them again. Only referenced when the Tool window library is present.
    internal static class AssetsTab
    {
        private static IDisposable _tab;
        private static List<TextureInfo> _textures;
        private static string _filter = "";
        private static Vector2 _scroll;
        private static string _status = "Press List to walk the loaded textures.";
        private static bool _showReplacements;

        public static void Install()
        {
            if (_tab != null)
            {
                return;
            }
            _tab = TW.AddTab(GameFonts.Guid, "Assets", Draw, 50);
        }

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = TW.Styles;
            float row = TW.RowHeight, pad = TW.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;

            if (GUI.Button(new Rect(x, y, 90, row), "List", s.Button))
            {
                _textures = AssetCatalog.Textures();
                _status = $"{_textures.Count} texture(s) loaded.";
            }
            if (GUI.Button(new Rect(x + 100, y, 170, row), "Apply replacements", s.Button))
            {
                int n = AssetReplacements.ApplyNow();
                _status = $"Replacements applied in {n} place(s).";
                _textures = null;
            }
            if (GUI.Button(new Rect(x + 280, y, 150, row), _showReplacements ? "Show textures" : "Show replacements", s.Button))
            {
                _showReplacements = !_showReplacements;
                _scroll = Vector2.zero;
            }
            _filter = GUI.TextField(new Rect(x + 440, y, Mathf.Max(80, w - 440), row), _filter ?? "", s.TextField);
            y += row + 8;

            string summary = $"{AssetReplacements.All.Count} replacement(s) from mods";
            if (AssetReplacements.ConflictCount > 0)
            {
                summary += $", {AssetReplacements.ConflictCount} overridden by another mod";
            }
            GUI.Label(new Rect(x, y, w, row), _status + "    |    " + summary, s.MutedLabel);
            y += row;

            var view = new Rect(x, y, w, area.yMax - pad - y);
            if (_showReplacements)
            {
                DrawReplacements(view, s, row);
            }
            else
            {
                DrawTextures(view, s, row);
            }
        }

        private static void DrawTextures(Rect view, ToolWindowStyles s, float row)
        {
            if (_textures == null)
            {
                GUI.Label(view, "Nothing listed yet.", s.MutedLabel);
                return;
            }
            var shown = new List<TextureInfo>();
            foreach (TextureInfo t in _textures)
            {
                if (string.IsNullOrEmpty(_filter) || t.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shown.Add(t);
                }
            }
            float inner = view.width - 20;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, inner, Mathf.Max(view.height, shown.Count * row)), false, false);
            float ry = 0;
            foreach (TextureInfo t in shown)
            {
                if (ry + row >= _scroll.y && ry <= _scroll.y + view.height)
                {
                    GUI.Label(new Rect(0, ry, inner * 0.45f, row), t.Name, s.Label);
                    GUI.Label(new Rect(inner * 0.45f, ry, inner * 0.2f, row), $"{t.Width}x{t.Height} {t.Format}", s.MutedLabel);
                    GUI.Label(new Rect(inner * 0.65f, ry, inner * 0.2f, row), $"{t.MaterialUsers} mat, {t.Sprites} sprite", s.MutedLabel);
                    if (t.Replaced)
                    {
                        GUI.Label(new Rect(inner * 0.85f, ry, inner * 0.15f, row), "replacement", s.MutedLabel);
                    }
                }
                ry += row;
            }
            GUI.EndScrollView();
        }

        private static void DrawReplacements(Rect view, ToolWindowStyles s, float row)
        {
            var all = new List<TextureReplacement>(AssetReplacements.All);
            all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (all.Count == 0)
            {
                GUI.Label(view, "No mod ships texture replacements (BepInEx/plugins/<Mod>/assets/textures/<name>.png).", s.WrappedLabel);
                return;
            }
            float inner = view.width - 20;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, inner, Mathf.Max(view.height, all.Count * row)), false, false);
            float ry = 0;
            foreach (TextureReplacement r in all)
            {
                GUI.Label(new Rect(0, ry, inner * 0.4f, row), r.Name, s.Label);
                GUI.Label(new Rect(inner * 0.4f, ry, inner * 0.3f, row), r.Mod, s.MutedLabel);
                string note = $"{r.Texture.width}x{r.Texture.height}, in {r.Applied} place(s)";
                if (r.Overrides.Count > 0)
                {
                    note += " - overrides " + string.Join(", ", r.Overrides);
                }
                GUI.Label(new Rect(inner * 0.7f, ry, inner * 0.3f, row), note, s.MutedLabel);
                ry += row;
            }
            GUI.EndScrollView();
        }
    }
}
