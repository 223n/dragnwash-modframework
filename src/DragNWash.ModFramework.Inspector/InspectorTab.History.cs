using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's History view: every edit this session, newest
    // first, with Undo/Redo and Copy, Undo last and Clear. In place of
    // the members, like Rigidbodies; the Export as overrides form opens here.
    // The words: Undo and Redo go one edit back or forward, Reset (on a row)
    // goes back to the value before the first edit.
    internal static partial class InspectorTab
    {
        private static bool _showHistory;
        private static Vector2 _scrollHistory;
        private const string ClearHistoryId = "inspector.history.clear";
        private static void DrawHistory(Rect pane, ToolWindowStyles s, float row)
        {
            TW.Fill(pane, TW.InsetColor);
            float x = pane.x + 4, w = pane.width - 8;
            int count = InspectorHistory.Count;
            float y = PaneHeader(pane, "HISTORY", $"{(count == 1 ? "1 edit" : count + " edits")} this session, newest first. Nothing is saved.", () => _showHistory = false, s, row);
            float bx = x;
            // With nothing in it there is nothing to undo or clear. The
            // buttons wrap in a narrow pane instead of running out of it.
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && InspectorHistory.Count > 0;
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Undo last"), "Undo last", false, s, row, "Undoes the latest edit of yours that still stands (Ctrl+Z)."))
            {
                UndoLast();
            }
            // Clearing loses every Undo and cannot be taken back, so it asks.
            if (FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Clear"), "Clear", TW.IsConfirming(ClearHistoryId), s, row))
            {
                TW.AskConfirm(ClearHistoryId);
            }
            GUI.enabled = wasEnabled;
            if (InspectorExport.Available && InspectorHistory.Count > 0
                && FlowButton(ref bx, ref y, x, w, ButtonWidth(s, "Export as overrides"), "Export as overrides", _exporting, s, row, "Writes these edits as a mod with no code, for the Overrides library."))
            {
                _exporting = !_exporting;
                _exportNote = "";
            }
            y += row + 4;
            if (TW.IsConfirming(ClearHistoryId))
            {
                int n = InspectorHistory.Count;
                string edits = n == 1 ? "1 edit" : n + " edits";
                if (TW.Confirm(new Rect(x, y, w, row), ClearHistoryId, $"Clear {edits}? They stay applied, and you can't undo them after.", "Yes, clear",
                    "Yes clears the history; Cancel or 5 s keeps it. Esc = Cancel."))
                {
                    InspectorHistory.Clear();
                    _exporting = false;
                    TW.ShowNotice($"Cleared {edits}. They stay applied in the game.", NoticeKind.Info, 8f);
                    InspectorPlugin.Log.LogInfo($"[inspector] History cleared ({edits}).");
                }
                y += row + 4;
            }
            if (_exporting)
            {
                y = DrawExportForm(x, y, w, s, row);
            }
            var view = new Rect(x, y, w, pane.yMax - y - 2);
            IReadOnlyList<InspectorHistory.Entry> all = InspectorHistory.All;
            float inner = view.width - 20;
            float lineH = row * 2;
            TW.ApplyScroll(view, ref _scrollHistory);
            _scrollHistory = GUI.BeginScrollView(view, _scrollHistory, new Rect(0, 0, inner, Mathf.Max(view.height, all.Count * lineH)), false, false);
            float ry = 0;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                InspectorHistory.Entry e = all[i];
                if (ry + lineH >= _scrollHistory.y && ry <= _scrollHistory.y + view.height)
                {
                    GUIStyle nameStyle = e.Reverted ? _mutedCell : _cell;
                    string by = e.By != null ? "  by " + e.By : "";
                    // Cut with "..." when too long; the whole line shows on the hint line.
                    HistoryLine(new Rect(0, ry, inner - 160, row), $"{e.Time:HH:mm:ss}  {e.Member}{by}" + (e.Reverted ? "  (undone)" : ""), nameStyle);
                    HistoryLine(new Rect(0, ry + row, inner - 160, row), $"{e.Label}:  {InspectorModel.Format(e.Before)}  ->  {InspectorModel.Format(e.After)}", _mutedCell);
                    // A change another mod made can be put back, but not made
                    // again from here: it is the mod's to repeat.
                    bool canPress = e.Undo == null || !e.Reverted;
                    if (canPress && GUI.Button(new Rect(inner - 154, ry + 2, 72, row - 4), e.Reverted ? "Redo" : "Undo", s.Button))
                    {
                        bool redo = e.Reverted;
                        string problem = redo ? InspectorHistory.Reapply(e) : InspectorHistory.Revert(e);
                        string result = problem != null
                            ? $"Couldn't {(redo ? "redo" : "undo")} {e.Member}: {problem}"
                            : redo ? $"Redid {e.Member}: {InspectorModel.Format(e.After)} again." : $"Undid {e.Member}: back to {InspectorModel.Format(e.Before)}.";
                        Tell(result, problem != null ? NoticeKind.Error : NoticeKind.Info);
                        InspectorPlugin.Log.LogInfo($"[inspector] {result}");
                    }
                    if (GUI.Button(new Rect(inner - 76, ry + 2, 72, row - 4), "Copy", s.Button))
                    {
                        GUIUtility.systemCopyBuffer = $"{e.Label} {e.Member} = {InspectorModel.Format(e.After)}";
                    }
                }
                ry += lineH;
            }
            GUI.EndScrollView();
        }

        private static void HistoryLine(Rect rect, string text, GUIStyle style)
        {
            string shown = Drawable(text);
            GUI.Label(rect, new GUIContent(TW.Elide(shown, style, rect.width), shown), style);
        }

        // Undo last and Ctrl+Z: the latest edit of this session's own that still stands.
        private static void UndoLast()
        {
            string result = InspectorHistory.Undo(out bool failed);
            Tell(result, failed ? NoticeKind.Error : NoticeKind.Info);
        }
    }
}
