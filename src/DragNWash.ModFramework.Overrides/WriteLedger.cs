using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework.Overrides
{
    // Who changed what, for the values this library writes: an overrides row
    // when an object appears, and a graph (or the console, or the page) calling
    // objects.member.set and its two neighbours.
    //
    // It is here for two reasons, both of them about mods living together:
    //
    // 1. When two mods write the same member, a person should be able to find
    //    out why their edit did nothing. The log says it once, and the
    //    objects.writes operation lists it, so the Mods screen can show it
    //    beside the graph that lost.
    // 2. A take-back must not undo somebody else's change. A graph that is
    //    switched off puts back the value it found - but if another graph wrote
    //    over it in the meantime, putting our value back would quietly undo
    //    theirs. So a take-back looks first: the value it wrote is still there,
    //    or it leaves it alone and says so.
    //
    // The key is the written object (component, material, GameObject) and the
    // member's name, so an overrides row and a graph meeting on one member meet
    // here as well, however each of them spelled the path.
    internal static class WriteLedger
    {
        internal sealed class Note
        {
            internal string Key;
            internal string Target;   // for people: "Dragon/Body [Light] intensity"
            internal string By;       // "overrides:Heavier Dragon", "graph:<mod>/<file>", "console"
            internal object Wrote;    // what we put there, to tell later whether it is still ours
            internal Func<object> Get;
            internal UnityEngine.Object Owner;   // null once the object is destroyed
            internal readonly List<string> Others = new List<string>();
        }

        private static readonly Dictionary<string, Note> Notes = new Dictionary<string, Note>(StringComparer.Ordinal);
        // key + the two names in one order, whoever wrote first: two mods taking
        // turns on one value are worth one line, not one line per direction.
        private static readonly HashSet<string> Said = new HashSet<string>(StringComparer.Ordinal);

        internal static string KeyOf(UnityEngine.Object target, string member)
        {
            return (target != null ? target.GetInstanceID() : 0) + "|" + member;
        }

        /// <summary>
        /// Notes a write. When someone else wrote this member before, their name
        /// comes back in <paramref name="other"/> (once per pair and member) so
        /// the caller can say it in the log.
        /// </summary>
        internal static Note Record(string key, string target, string by, object wrote, Func<object> get, UnityEngine.Object owner, out string other)
        {
            other = null;
            Sweep();
            if (!Notes.TryGetValue(key, out Note note))
            {
                note = new Note { Key = key };
                Notes[key] = note;
            }
            else if (note.By != null && note.By != by && note.Owner != null)
            {
                if (!note.Others.Contains(note.By))
                {
                    note.Others.Add(note.By);
                }
                string pair = string.CompareOrdinal(note.By, by) <= 0 ? note.By + "|" + by : by + "|" + note.By;
                if (Said.Add(key + "|" + pair))
                {
                    other = note.By;
                }
            }
            note.Target = target;
            note.By = by;
            note.Wrote = wrote;
            note.Get = get;
            note.Owner = owner;
            return note;
        }

        /// <summary>
        /// True when this caller is still the last one to have written the
        /// member, and the value is still the one it wrote: the object lives,
        /// nobody else has written over it, and nothing outside this library
        /// (the game's own script, a hand edit) has changed it since.
        /// </summary>
        /// <remarks>
        /// The question is asked of the note, not of one write: a caller that
        /// wrote the same member several times restores the value it found the
        /// first time, and it is the note that knows what it last put there.
        /// </remarks>
        internal static bool StillOurs(Note note, string by)
        {
            if (note == null || note.Owner == null) return false;
            if (note.By != by) return false;
            try
            {
                return Equals(note.Get(), note.Wrote);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Who wrote a member last, for saying who changed it since.</summary>
        internal static string LastWriter(Note note) => note != null ? note.By : null;

        /// <summary>Forgets a note after its value was put back.</summary>
        internal static void Forget(Note note)
        {
            if (note != null && Notes.TryGetValue(note.Key, out Note held) && ReferenceEquals(held, note))
            {
                Notes.Remove(note.Key);
            }
        }

        // Objects come and go with the levels, and a note for one that is gone
        // says nothing to anybody. They are cleared out when there are enough to
        // be worth the walk, so a session that runs for hours does not carry
        // every dragon it ever washed.
        private const int SweepAbove = 512;

        private static void Sweep()
        {
            if (Notes.Count < SweepAbove)
            {
                return;
            }
            foreach (string key in Notes.Where(n => n.Value.Owner == null).Select(n => n.Key).ToList())
            {
                Notes.Remove(key);
            }
        }

        /// <summary>Everything written this session that is still standing, for <c>objects.writes</c>.</summary>
        internal static List<object> Report()
        {
            return Notes.Values
                .Where(n => n.Owner != null)
                .Select(n => (object)new Dictionary<string, object>
                {
                    ["target"] = n.Target,
                    ["by"] = n.By,
                    ["also"] = n.Others.ToList(),
                    ["value"] = n.Wrote == null ? null : Convert.ToString(n.Wrote, System.Globalization.CultureInfo.InvariantCulture),
                })
                .ToList();
        }

    }
}
