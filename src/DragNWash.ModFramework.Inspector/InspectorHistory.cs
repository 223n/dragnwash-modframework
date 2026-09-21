using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DragNWash.ModFramework.Inspector
{
    // Every edit made through the Inspector this session: what it was before
    // the first edit (the original), before this edit, and after. Rows and the
    // gizmo record here; the History view lists it; a row's menu and Reset
    // read it back. Keyed by the target object and the member, so a value
    // edited, deselected and selected again still knows its original.
    internal static class InspectorHistory
    {
        internal sealed class Entry
        {
            public string Key;
            public string Label;      // where: object path and component
            public string Member;     // the member (or element) name
            public object Before, After;
            public DateTime Time;
            public Func<object> Get;
            public Action<object> Set;
            public bool Reverted;
            // Where the edit was, in the terms of an overrides file, taken when
            // it was made (the object may be gone by the time it is exported).
            public InspectorExport.Place Where;
            public object Target;
            // Set for a change another mod made through an operation: who asked
            // (graph:<mod>/<file>, console, page) and the registry's own way of
            // putting it back. Such an entry has no Set of its own, cannot be
            // done again once put back, and is not exported as an override: it
            // belongs to the mod that made it, not to this session's edits.
            public string By;
            public Func<bool> Undo;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        // How long one change goes on being the same change, in seconds.
        private const double Gather = 3;
        private static readonly Dictionary<string, object> Originals = new Dictionary<string, object>(StringComparer.Ordinal);

        internal static IReadOnlyList<Entry> All => Entries;
        internal static int Count => Entries.Count;

        internal static string KeyOf(object target, string member)
        {
            int id = target is UnityEngine.Object uo ? uo.GetInstanceID() : RuntimeHelpers.GetHashCode(target);
            return id + "|" + member;
        }

        internal static void Record(object target, string label, string member, object before, object after, Func<object> get, Action<object> set)
        {
            string key = KeyOf(target, member);
            if (!Originals.ContainsKey(key))
            {
                Originals[key] = before;
            }
            Entries.Add(new Entry { Key = key, Label = label, Member = member, Before = before, After = after, Time = DateTime.Now, Get = get, Set = set, Target = target, Where = InspectorExport.PlaceOf(target, member) });
            if (Entries.Count > 500)
            {
                Entries.RemoveAt(0);
            }
        }

        /// <summary>
        /// A change another mod made through a write operation, as the registry
        /// tells it (<see cref="Operations.Written"/>). It is listed beside the
        /// edits made by hand so a person can see that a graph moved something,
        /// and put that one change back without stopping the whole graph.
        /// </summary>
        internal static void RecordWrite(OperationTakeBack w)
        {
            if (w == null)
            {
                return;
            }
            string key = "op|" + w.Operation + "|" + w.Label;
            // A colour dragged in the editor, or a graph writing in a loop, is
            // one change being made over and over. Rows for each step would
            // push this session's own edits out of the list and say nothing
            // more than the last one does, so they are gathered into one: the
            // value it started from stays, and with it the way back.
            Entry last = Entries.Count > 0 ? Entries[Entries.Count - 1] : null;
            if (last != null && last.Key == key && last.By == w.Caller && !last.Reverted
                && (DateTime.Now - last.Time).TotalSeconds < Gather)
            {
                last.After = w.After;
                last.Time = DateTime.Now;
                return;
            }
            Entries.Add(new Entry
            {
                Key = key,
                Label = w.Label ?? "",
                Member = w.Operation,
                Before = w.Before,
                After = w.After,
                Time = DateTime.Now,
                By = w.Caller,
                Undo = w.Run,
            });
            if (Entries.Count > 500)
            {
                Entries.RemoveAt(0);
            }
        }

        internal static bool TryOriginal(object target, string member, out object original)
        {
            return Originals.TryGetValue(KeyOf(target, member), out original);
        }

        // What the member held just before its latest edit.
        internal static bool TryPrevious(object target, string member, out object previous)
        {
            string key = KeyOf(target, member);
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].Key == key)
                {
                    previous = Entries[i].Before;
                    return true;
                }
            }
            previous = null;
            return false;
        }

        internal static void ForgetOriginal(object target, string member)
        {
            Originals.Remove(KeyOf(target, member));
        }

        internal static List<Entry> For(object target, string member)
        {
            string key = KeyOf(target, member);
            return Entries.FindAll(e => e.Key == key);
        }

        // Puts back the value before the entry; the entry stays, marked.
        internal static string Revert(Entry e)
        {
            if (e.Undo != null)
            {
                // The write's own take-back: it knows whether the value is still
                // the one it wrote, and leaves somebody else's change alone.
                // False means there was nothing to put back: the object is gone,
                // or somebody wrote the value after this change did. The entry
                // stays as it is, so the row still says what happened.
                bool ok = e.Undo();
                e.Reverted = ok;
                return ok ? null : "it was left as it is: the value is not the one that change made any more";
            }
            try
            {
                e.Set(e.Before);
                e.Reverted = true;
                return null;
            }
            catch (Exception ex)
            {
                return (ex.InnerException ?? ex).Message;
            }
        }

        internal static string Reapply(Entry e)
        {
            if (e.Undo != null)
            {
                return $"{e.By} made this change; it cannot be made again from here";
            }
            try
            {
                e.Set(e.After);
                e.Reverted = false;
                return null;
            }
            catch (Exception ex)
            {
                return (ex.InnerException ?? ex).Message;
            }
        }

        // The latest edit made here that still stands. A change another mod made
        // is passed over: "Undo last" is for this session's own edits, and a
        // graph's write is put back from its own row, or by stopping the graph.
        internal static string Undo()
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!Entries[i].Reverted && Entries[i].Undo == null)
                {
                    return Revert(Entries[i]) ?? $"Reverted {Entries[i].Member}.";
                }
            }
            return "Nothing of your own to undo.";
        }

        internal static void Clear()
        {
            Entries.Clear();
            Originals.Clear();
        }
    }
}
