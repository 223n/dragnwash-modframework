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
        // One line per row: the window's label styles wrap, and a long texture
        // name in a narrow window ran into the rows below it.
        private static GUIStyle _cell, _mutedCell, _accentCell, _warnCell, _warnSmall;

        private static void EnsureCells(ToolWindowStyles s)
        {
            if (_cell != null)
            {
                return;
            }
            _cell = new GUIStyle(s.Label) { wordWrap = false, clipping = TextClipping.Clip };
            _mutedCell = new GUIStyle(s.MutedLabel) { wordWrap = false, clipping = TextClipping.Clip };
            _accentCell = new GUIStyle(_cell);
            _accentCell.normal.textColor = TW.AccentColor;
            _accentCell.hover.textColor = TW.AccentColor;
            _warnCell = new GUIStyle(_cell);
            _warnCell.normal.textColor = TW.WarningColor;
            _warnCell.hover.textColor = TW.WarningColor;
            _warnSmall = new GUIStyle(s.Hint) { fontStyle = FontStyle.Bold };
            _warnSmall.normal.textColor = TW.WarningColor;
        }
        private static List<TextureInfo> _textures;
        private static string _filter = "";
        private static Vector2 _scroll;
        private static string _status = "Press List to walk the loaded textures.";
        private static bool _showReplacements;

        // The Inspector's Go on a texture: list, filter to the name, open the tab.
        internal static void ShowTexture(string textureName)
        {
            _textures = AssetCatalog.Textures();
            _filter = textureName ?? "";
            _showReplacements = false;
            _scroll = Vector2.zero;
            _status = $"{Plural(_textures.Count, "texture", "textures")} loaded; showing \"{_filter}\".";
            TW.Open("Assets");
        }

        // The Inspector library, when it is loaded: a texture row's Inspect
        // button opens the texture in its Objects view.
        private static System.Reflection.MethodInfo _inspect;
        private static bool _inspectLookedUp;
        private static System.Reflection.MethodInfo InspectMethod()
        {
            if (!_inspectLookedUp)
            {
                _inspectLookedUp = true;
                Type type = Type.GetType("DragNWash.ModFramework.Inspector.Inspector, DragNWash.ModFramework.Inspector", false);
                _inspect = type?.GetMethod("Inspect", new[] { typeof(UnityEngine.Object) });
            }
            return _inspect;
        }

        public static void Install()
        {
            if (_tab != null)
            {
                return;
            }
            _tab = TW.AddTab(GameFonts.Guid, "Assets", Draw, 50);
            TW.AddCommand(GameFonts.Guid, "assets", "assets textures [filter] | assets replacements | assets apply | assets reload", Command,
                args => args.Length == 1 ? new[] { "textures", "replacements", "apply", "reload" } : new string[0]);
        }

        private static string Command(string[] args)
        {
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            switch (what)
            {
                case "textures":
                {
                    string filter = args.Length > 1 ? args[1] : null;
                    var lines = new List<string>();
                    foreach (TextureInfo t in AssetCatalog.Textures())
                    {
                        if (filter == null || t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            lines.Add($"{t.Name}  {t.Width}x{t.Height} {t.Format}  {t.MaterialUsers} mat, {t.Sprites} sprite{(t.Replaced ? "  replacement" : "")}");
                        }
                    }
                    return lines.Count == 0 ? "No texture matches." : string.Join("\n", lines);
                }
                case "replacements":
                {
                    var lines = new List<string>();
                    foreach (TextureReplacement r in AssetReplacements.All)
                    {
                        lines.Add($"{r.Name}  from {r.Mod}" + (r.Language != null ? $" ({r.Language})" : "") + $"  in {r.Applied} place(s)" + (r.Overrides.Count > 0 ? "  overrides " + string.Join(", ", r.Overrides) : "") + (r.Problem != null ? "  NOT reloaded: " + r.Problem : ""));
                    }
                    if (AssetReplacements.PendingLanguage != null)
                    {
                        lines.Add($"Pictures for \"{AssetReplacements.PendingLanguage}\" apply after a restart (Direct3D 12).");
                    }
                    return lines.Count == 0 ? "No mod ships texture replacements." : string.Join("\n", lines);
                }
                case "apply":
                    return $"Replacements applied in {AssetReplacements.ApplyNow()} place(s).";
                case "reload":
                {
                    var lines = new List<string>();
                    foreach (ReloadResult r in AssetReplacements.ReloadFiles())
                    {
                        lines.Add($"{r.Name}: {r.Status}");
                    }
                    return string.Join("\n", lines);
                }
                default:
                    return "assets textures [filter] | assets replacements | assets apply | assets reload";
            }
        }

        private static void Draw(Rect area)
        {
            ToolWindowStyles s = TW.Styles;
            EnsureCells(s);
            float row = TW.RowHeight, pad = TW.Padding;
            float x = area.x + pad, y = area.y + pad, w = area.width - 2 * pad;
            // A reload reads a file a frame; the tab waits for it, and says how far it is.
            if (AssetReplacements.Reloading)
            {
                TW.Busy("Reloading files...", AssetReplacements.ReloadingName == null ? null
                    : $"{AssetReplacements.ReloadingName}  ({AssetReplacements.ReloadingDone + 1} of {AssetReplacements.ReloadingTotal})");
            }

            // First row: which list shows, like tabs, with how many each has,
            // and the filter in what is left of the row (or on a row of its own).
            // Second row: what to do. Both wrap in a narrow window.
            EnsureRows();
            float bx = x;
            string texturesLabel = _textures != null ? $"Textures ({Count(_textures.Count)})" : "Textures";
            if (TW.FlowButton(ref bx, ref y, x, w, new GUIContent(texturesLabel, "Every texture loaded right now. Click a name to see it."), !_showReplacements))
            {
                _showReplacements = false;
            }
            if (TW.FlowButton(ref bx, ref y, x, w, new GUIContent($"Replacements ({Count(_rows.Count)})", "The PNGs mods ship, and where each one is in use."), _showReplacements))
            {
                _showReplacements = true;
            }
            // How many of them are yellow or red in the list, beside its switch.
            if (_toCheck > 0)
            {
                string check = $"{Count(_toCheck)} to check";
                float cw = _warnSmall.CalcSize(new GUIContent(check)).x;
                if (bx + cw <= x + w)
                {
                    var checkRect = new Rect(bx, y, cw, row);
                    GUI.Label(checkRect, check, _warnSmall);
                    TW.Hint(checkRect, "Replacements that aren't in use or didn't load. They're yellow or red under Replacements.");
                    bx += cw + 8;
                }
            }
            if (x + w - bx < 140)
            {
                bx = x;
                y += row + 2;
            }
            // The text field blends into the panel; an underline and a placeholder show where it is.
            _filter = TW.FilterField(new Rect(bx, y, x + w - bx, row), _filter, _showReplacements ? "Filter by name or mod" : "Filter by name", s);
            y += row + 6;

            bx = x;
            if (TW.FlowButton(ref bx, ref y, x, w, new GUIContent("List again", "Lists the textures loaded right now.")))
            {
                _textures = AssetCatalog.Textures();
                _status = $"{Plural(_textures.Count, "texture", "textures")} loaded.";
                _selected = null;
            }
            if (TW.FlowButton(ref bx, ref y, x, w, new GUIContent("Apply replacements", "Puts the replacements into materials and sprites that still show the original. Uploads nothing, so it's always safe.")))
            {
                int n = AssetReplacements.ApplyNow();
                _status = $"Replacements applied in {n} place(s).";
                _textures = null;
            }
            bool wasEnabled = GUI.enabled;
            GUI.enabled = !AssetReplacements.ReloadDisabled && !AssetReplacements.Reloading;
            string reloadTip = GameFonts.RuntimeUploadsAreSafe
                ? "Reads the PNGs you changed again and swaps them in."
                : "Reads the PNGs you changed again and swaps them in. -force-d3d11 makes this safe.";
            if (TW.FlowButton(ref bx, ref y, x, w, new GUIContent("Reload files", reloadTip)))
            {
                if (AssetsLibraryPlugin.Instance != null)
                {
                    _status = "Reloading files...";
                    AssetsLibraryPlugin.Instance.StartCoroutine(AssetReplacements.ReloadFilesOverFrames(Reloaded));
                }
                else
                {
                    Reloaded(AssetReplacements.ReloadFiles());
                }
            }
            GUI.enabled = wasEnabled;
            y += row + 6;

            GUI.Label(new Rect(x, y, w, row), TW.Elide(_status, s.MutedLabel, w), s.MutedLabel);
            y += row;
            string reloadNote = AssetReplacements.ReloadDisabled
                ? "Reload files: " + AssetReplacements.ReloadDisabledReason
                : GameFonts.RuntimeUploadsAreSafe
                    ? "Reload files re-reads changed PNGs and uploads them; Apply replacements only re-points materials and sprites."
                    : "Reload files uploads textures while the game runs, which can crash it on Direct3D 12; Apply replacements is always safe. Work with -force-d3d11 to reload freely.";
            // Wraps on narrow windows; take as many rows as it needs.
            float noteHeight = Mathf.Max(row, s.WrappedLabel.CalcHeight(new GUIContent(reloadNote), w));
            GUI.Label(new Rect(x, y, w, noteHeight), reloadNote, s.WrappedLabel);
            y += noteHeight + 4;

            var view = new Rect(x, y, w, area.yMax - pad - y);
            if (_showReplacements)
            {
                DrawReplacements(view, s, row);
            }
            else
            {
                // A selected texture gets a preview: beside the list where there
                // is room, above it in a narrow window.
                if (_selected != null && _selected.Texture)
                {
                    if (view.width >= 640)
                    {
                        float pw = Mathf.Clamp(view.width * 0.38f, 220, 420);
                        DrawPreview(new Rect(view.xMax - pw, view.y, pw, view.height), s, row);
                        view.width -= pw + 8;
                    }
                    else
                    {
                        float ph = Mathf.Min(220, view.height * 0.45f);
                        DrawPreview(new Rect(view.x, view.y, view.width, ph), s, row);
                        view.y += ph + 8;
                        view.height -= ph + 8;
                    }
                }
                DrawTextures(view, s, row);
            }
        }

        private static TextureInfo _selected;

        // 1,234 whatever the game's culture, and the word that goes with the number.
        private static string Count(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        private static string Plural(int n, string one, string many) => $"{Count(n)} {(n == 1 ? one : many)}";

        // After Reload files: the count on the status line, and a notice - a
        // warning when a file did not load, which the replacements list then shows.
        private static void Reloaded(IReadOnlyList<ReloadResult> results)
        {
            int n = 0, bad = 0;
            foreach (ReloadResult r in results)
            {
                if (r.Status == "reloaded") n++;
                else if (r.Status != "unchanged") bad++;
            }
            _status = $"Reloaded {n} file(s)" + (bad > 0 ? $", {bad} with problems (see Show replacements)" : "") + ".";
            _showReplacements = bad > 0 || _showReplacements;
            _textures = null;
            TW.ShowNotice(_status, bad > 0 ? NoticeKind.Warning : NoticeKind.Info, bad > 0 ? 12f : 6f);
        }

        // The texture as it is on the GPU, scaled to fit, with its facts. Drawing
        // a loaded texture uploads nothing, so this is safe on Direct3D 12.
        private static void DrawPreview(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.PanelColor);
            TextureInfo t = _selected;
            float x = pane.x + 6, y = pane.y + 4, w = pane.width - 12;
            GUI.Label(new Rect(x, y, w - 60, row), t.Name, _cell);
            if (GUI.Button(new Rect(pane.xMax - 58, y + 2, 52, row - 4), "Close", s.Button))
            {
                _selected = null;
                return;
            }
            y += row;
            GUI.Label(new Rect(x, y, w, row), $"{t.Width}x{t.Height}  {t.Format}  {(t.Readable ? "readable" : "GPU only")}  {t.MaterialUsers} mat, {t.Sprites} sprite{(t.Replaced ? "  replacement" : "")}", _mutedCell);
            y += row;
            var box = new Rect(x, y, w, pane.yMax - y - 6);
            if (box.height < 24)
            {
                return;
            }
            // A dark and a light square behind it, so a transparent or dark image still reads.
            TW.Fill(box, new Color(0.18f, 0.2f, 0.24f));
            TW.Fill(new Rect(box.x, box.y, box.width / 2, box.height / 2), new Color(0.26f, 0.28f, 0.32f));
            TW.Fill(new Rect(box.x + box.width / 2, box.y + box.height / 2, box.width / 2, box.height / 2), new Color(0.26f, 0.28f, 0.32f));
            float scale = Mathf.Min(box.width / t.Width, box.height / t.Height);
            float dw = t.Width * scale, dh = t.Height * scale;
            var fit = new Rect(box.x + (box.width - dw) / 2, box.y + (box.height - dh) / 2, dw, dh);
            GUI.DrawTexture(fit, t.Texture, ScaleMode.StretchToFill, true);
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
                    bool selected = ReferenceEquals(t, _selected);
                    if (selected)
                    {
                        TW.Fill(new Rect(0, ry, inner, row), TW.PanelColor);
                    }
                    // The name is a button: it selects the texture for the preview.
                    // A long one is cut, and shown whole on the hint line.
                    var nameRect = new Rect(0, ry, inner * 0.45f - 6, row);
                    string name = TW.Elide(t.Name, _cell, nameRect.width);
                    if (name != t.Name) TW.Hint(nameRect, t.Name);
                    if (GUI.Button(nameRect, name, selected ? _accentCell ?? _cell : _cell))
                    {
                        _selected = selected ? null : t;
                    }
                    GUI.Label(new Rect(inner * 0.45f, ry, inner * 0.2f - 6, row), $"{t.Width}x{t.Height} {t.Format}", _mutedCell);
                    GUI.Label(new Rect(inner * 0.65f, ry, inner * 0.2f - 6, row), $"{t.MaterialUsers} mat, {t.Sprites} sprite", _mutedCell);
                    if (t.Replaced)
                    {
                        GUI.Label(new Rect(inner * 0.85f, ry, inner * 0.15f - 70, row), "replacement", _mutedCell);
                    }
                    if (InspectMethod() != null && GUI.Button(new Rect(inner - 66, ry + 2, 66, row - 4), "Inspect", s.Button))
                    {
                        // The Inspector's Objects view, whose Used by lists its materials and sprites.
                        InspectMethod().Invoke(null, new object[] { t.Texture });
                    }
                }
                ry += row;
            }
            GUI.EndScrollView();
        }

        // ---- the replacements list ------------------------------------------------------

        // What a replacement row says about its file: in use, not in use though
        // nothing is broken (yellow), switched off (dim), or unreadable (red).
        private enum RowState { InUse, NotUsedYet, Loses, Off, Failed }

        private sealed class ReplacementRow
        {
            public string Name, Mod, Status, Hint;
            public RowState State;
            public bool Loser;
        }

        // Built again only when the replacements changed (AssetReplacements.Revision),
        // and filtered again only when the filter changed too, not on every draw.
        private static List<ReplacementRow> _rows = new List<ReplacementRow>();
        private static int _rowsRevision = -1;
        private static int _toCheck;
        private static List<ReplacementRow> _rowsShown;
        private static string _rowsShownFilter;
        private static Vector2 _scrollReplacements;

        private static void EnsureRows()
        {
            if (_rowsRevision == AssetReplacements.Revision)
            {
                return;
            }
            _rowsRevision = AssetReplacements.Revision;
            _rowsShown = null;
            var all = new List<TextureReplacement>(AssetReplacements.All);
            all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            // A mod that lost a clash only shows up in the winner's Overrides; it
            // gets a row of its own (unless it has one), so its author finds it
            // by filtering for the mod.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TextureReplacement r in all)
            {
                seen.Add(r.Name + "|" + r.Mod);
            }
            HashSet<string> loaded = null;
            var rows = new List<ReplacementRow>();
            int toCheck = 0;
            foreach (TextureReplacement r in all)
            {
                var row = new ReplacementRow { Name = r.Name, Mod = r.Language != null ? $"{r.Mod} ({r.Language})" : r.Mod };
                TextureReplacement now = AssetReplacements.EffectiveFor(r.Name);
                if (r.Problem != null)
                {
                    row.State = RowState.Failed;
                    row.Status = "Not reloaded: " + r.Problem;
                    row.Hint = $"The file couldn't be read again, so the picture from before stays. {r.Problem}";
                }
                else if (AssetReplacements.IsSwitchedOff(r))
                {
                    row.State = RowState.Off;
                    row.Status = "Off: this mod's language pictures are switched off";
                    row.Hint = "The mod has switched its language pictures off, usually with a setting of its own.";
                }
                else if (now != null && now != r)
                {
                    row.State = RowState.Loses;
                    row.Status = $"Not used: {now.Mod} wins";
                    row.Hint = now.Language != null && r.Language == null
                        ? $"While the language is {now.Language}, the picture from {now.Mod} is used instead."
                        : $"{now.Mod} also ships \"{r.Name}\", and its file is the one used.";
                }
                else if (r.Applied == 0)
                {
                    if (loaded == null)
                    {
                        loaded = LoadedTextureNames();
                    }
                    row.State = RowState.NotUsedYet;
                    row.Status = "Not used yet";
                    row.Hint = loaded.Contains(r.Name)
                        ? $"A texture called \"{r.Name}\" is loaded, but nothing has taken this picture yet. Try Apply replacements."
                        : $"No loaded texture is called \"{r.Name}\". Check the file name, or open the scene that uses it.";
                }
                else
                {
                    row.State = RowState.InUse;
                    row.Status = $"In {Plural(r.Applied, "place", "places")}";
                    row.Hint = r.Texture != null ? $"{r.Texture.width}x{r.Texture.height}, {r.Path}" : r.Path;
                }
                rows.Add(row);
                if (row.State != RowState.InUse && row.State != RowState.Off)
                {
                    toCheck++;
                }
                string winner = (now ?? r).Mod;
                foreach (string loser in r.Overrides)
                {
                    if (seen.Add(r.Name + "|" + loser))
                    {
                        rows.Add(new ReplacementRow
                        {
                            Name = r.Name, Mod = loser, Loser = true, State = RowState.Loses,
                            Status = $"Not used: {winner} wins",
                            Hint = $"{winner} also ships \"{r.Name}\", and its file is the one used.",
                        });
                        toCheck++;
                    }
                }
            }
            _rows = rows;
            _toCheck = toCheck;
        }

        // Names of the game's own textures that are loaded, for "Not used yet".
        private static HashSet<string> LoadedTextureNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (t != null && !string.IsNullOrEmpty(t.name) && !AssetReplacements.IsReplacement(t))
                {
                    names.Add(t.name);
                }
            }
            return names;
        }

        // A line on the panel colour with a 3 px bar on its left, as the window's notices look.
        private static void Band(Rect rect, Color bar)
        {
            TW.Fill(rect, TW.PanelColor);
            TW.Fill(new Rect(rect.x, rect.y, 3, rect.height), bar);
        }

        private static void DrawReplacements(Rect view, ToolWindowStyles s, float row)
        {
            // Direct3D 12 can't load the new language's pictures while the game runs.
            if (AssetReplacements.PendingLanguage != null)
            {
                var band = new Rect(view.x, view.y, view.width, row);
                Band(band, TW.WarningColor);
                string text = TW.Drawable($"Pictures for \"{AssetReplacements.PendingLanguage}\" apply after a restart (Direct3D 12).");
                GUI.Label(new Rect(band.x + 10, band.y, band.width - 14, row), TW.Elide(text, _cell, band.width - 14), _cell);
                view.y += row + 6;
                view.height -= row + 6;
            }
            if (_rows.Count == 0)
            {
                GUI.Label(view, "No mod ships texture replacements (BepInEx/plugins/<Mod>/assets/textures/<name>.png).", s.WrappedLabel);
                return;
            }
            if (_rowsShown == null || _rowsShownFilter != _filter)
            {
                _rowsShownFilter = _filter;
                _rowsShown = new List<ReplacementRow>();
                foreach (ReplacementRow r in _rows)
                {
                    if (string.IsNullOrEmpty(_filter) || r.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0 || r.Mod.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _rowsShown.Add(r);
                    }
                }
            }
            List<ReplacementRow> shown = _rowsShown;
            if (shown.Count == 0)
            {
                GUI.Label(new Rect(view.x, view.y, view.width, row), TW.Elide(TW.Drawable($"No replacement's name or mod has \"{_filter}\" in it."), s.MutedLabel, view.width), s.MutedLabel);
                return;
            }
            // Name, mod and state; a narrow window leaves the mod out.
            float inner = view.width - 20;
            bool wide = inner >= 580;
            const float gap = 8;
            float nameW, modW, statusW;
            if (wide)
            {
                float avail = inner - 2 * gap;
                nameW = avail / 3f;
                modW = avail * 0.8f / 3f;
                statusW = avail - nameW - modW;
            }
            else
            {
                nameW = (inner - gap) / 2f;
                modW = 0;
                statusW = inner - gap - nameW;
            }
            _scrollReplacements = GUI.BeginScrollView(view, _scrollReplacements, new Rect(0, 0, inner, Mathf.Max(view.height, shown.Count * row)), false, false);
            // Only the rows in sight are drawn.
            int first = Mathf.Clamp((int)(_scrollReplacements.y / row), 0, shown.Count);
            int last = Mathf.Min(shown.Count, first + Mathf.CeilToInt(view.height / row) + 1);
            for (int i = first; i < last; i++)
            {
                ReplacementRow r = shown[i];
                float ry = i * row;
                float cx = 0;
                GUIStyle nameStyle = r.Loser ? _mutedCell : _cell;
                string name = TW.Drawable(r.Name);
                string shownName = TW.Elide(name, nameStyle, nameW);
                GUI.Label(new Rect(cx, ry, nameW, row), shownName, nameStyle);
                cx += nameW + gap;
                if (wide)
                {
                    GUI.Label(new Rect(cx, ry, modW, row), TW.Elide(TW.Drawable(r.Mod), _mutedCell, modW), _mutedCell);
                    cx += modW + gap;
                }
                GUIStyle statusStyle = r.State == RowState.Failed ? s.Danger
                    : r.State == RowState.Off ? _mutedCell
                    : r.State == RowState.InUse ? _cell
                    : _warnCell;
                string status = TW.Drawable(r.Status);
                string shownStatus = TW.Elide(status, statusStyle, statusW);
                GUI.Label(new Rect(cx, ry, statusW, row), shownStatus, statusStyle);
                // The row explains itself on the hint line, with whatever was cut
                // off; the text is only put together for the row pointed at.
                var rowRect = new Rect(0, ry, inner, row);
                if (Event.current != null && rowRect.Contains(Event.current.mousePosition))
                {
                    string hint = r.Hint;
                    if (shownStatus != status) hint = r.Status + ". " + hint;
                    if (shownName != name || !wide) hint = $"{r.Name} ({r.Mod}): {hint}";
                    TW.Hint(TW.Drawable(hint));
                }
            }
            GUI.EndScrollView();
        }
    }
}
