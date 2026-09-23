using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System.Collections.Generic;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's "Export as overrides" form, opened from History:
    // name, author, description and the Export button that writes a mod with
    // no code from the session's edits (InspectorExport). Optional; shows only
    // with the Overrides library installed.
    internal static partial class InspectorTab
    {
        private static bool _exporting;
        private static string _exportName = "My changes";
        private static string _exportAuthor = "";
        private static string _exportDescription = "";
        private static string _exportNote = "";
        private static bool _exportOk;

        // The mod's name, author and description, what will be written, and Export.
        private static float DrawExportForm(float x, float y, float w, ToolWindowStyles s, float row)
        {
            List<InspectorExport.Row> rows = InspectorExport.Collect(out List<string> skipped);
            y = WrappedLine($"A mod with no code from these edits: {rows.Count} value(s), as they are now{(skipped.Count > 0 ? $"; {skipped.Count} edit(s) cannot be overrides ({skipped[0]}{(skipped.Count > 1 ? ", ..." : "")})" : "")}. It goes to BepInEx/plugins/<name>.", x, y, w, _mutedCell, row);
            float labelW = 100;
            _exportName = ExportField("Name", _exportName, x, ref y, w, labelW, s, row);
            _exportAuthor = ExportField("Author", _exportAuthor, x, ref y, w, labelW, s, row);
            _exportDescription = ExportField("Description", _exportDescription, x, ref y, w, labelW, s, row);
            // With nothing to write, a plain disabled button and the reason beside it.
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && rows.Count > 0;
            bool export = GUI.Button(new Rect(x, y, 100, row), "Export", s.Button);
            GUI.enabled = wasEnabled;
            if (rows.Count == 0)
            {
                string why = skipped.Count > 0 ? "None of these edits can be overrides." : "Nothing to export yet.";
                GUI.Label(new Rect(x + 206, y, w - 206, row), TW.Elide(why, _mutedCell, w - 206), _mutedCell);
            }
            if (export && rows.Count > 0)
            {
                _exportNote = InspectorExport.Write(_exportName, _exportAuthor, _exportDescription, out _exportOk);
                Tell(_exportNote, _exportOk ? NoticeKind.Info : NoticeKind.Error);
            }
            if (GUI.Button(new Rect(x + 108, y, 90, row), "Cancel", s.Button))
            {
                _exporting = false;
            }
            y += row + 4;
            if (!string.IsNullOrEmpty(_exportNote))
            {
                y = WrappedLine(_exportNote, x, y, w, _exportOk ? _mutedCell : _errorCell, row);
            }
            return y + 4;
        }

        private static string ExportField(string label, string value, float x, ref float y, float w, float labelW, ToolWindowStyles s, float row)
        {
            GUI.Label(new Rect(x, y, labelW, row), label, _mutedCell);
            var field = new Rect(x + labelW, y, w - labelW, row);
            // Named, so typing in it sets off no shortcut.
            GUI.SetNextControlName("DnWInspectExport" + label);
            string next = GUI.TextField(field, value ?? "", s.TextField);
            TW.Underline(field);
            y += row + 2;
            return next;
        }
    }
}
