using System;
using System.Collections.Generic;

namespace DragNWash.ModFramework.Dialogue
{
    /// <summary>How a <see cref="LineResolver"/> recognised a line, strongest first.</summary>
    public enum LineMatchLayer
    {
        /// <summary>The Yarn line ID matched. The text may have been edited since the record was made.</summary>
        LineId,
        /// <summary>The exact text matched (<see cref="LineKey.Hash"/>).</summary>
        Hash,
        /// <summary>Only punctuation, capitalisation, spacing or tags differ (<see cref="LineKey.NormalizedHash"/>).</summary>
        Normalized,
        /// <summary>The text is close (<see cref="LineKey.Fingerprint"/>) and the record is the only candidate in the node.</summary>
        Fuzzy,
    }

    /// <summary>What a mod knows about one line: its keys and where it sits in the script.</summary>
    public sealed class LineRecord
    {
        /// <summary>Yarn line ID ("line:6046bedf"), or null when the record predates line IDs.</summary>
        public string LineId { get; set; }

        /// <summary><see cref="LineKey.Hash"/> of the displayed text, or null.</summary>
        public string Hash { get; set; }

        /// <summary><see cref="LineKey.NormalizedHash"/> of the displayed text, or null.</summary>
        public string NormalizedHash { get; set; }

        /// <summary><see cref="LineKey.Fingerprint"/> of the displayed text, or 0 when unknown.</summary>
        public ulong Fingerprint { get; set; }

        /// <summary>Length of the normalized text, or 0 when unknown. Guards fuzzy matching of short lines.</summary>
        public int NormalizedLength { get; set; }

        /// <summary>Yarn node the line was seen in, or null.</summary>
        public string Node { get; set; }

        /// <summary>Speaker as written in the script, or null.</summary>
        public string Speaker { get; set; }

        /// <summary>Whatever the mod keeps for this line (a translation, an index...).</summary>
        public object Payload { get; set; }
    }

    /// <summary>The outcome of <see cref="LineResolver.Resolve(DialogueLine, string)"/>.</summary>
    public sealed class LineMatch
    {
        internal LineMatch(LineRecord record, LineMatchLayer layer, bool needsReview)
        {
            Record = record;
            Layer = layer;
            NeedsReview = needsReview;
        }

        /// <summary>The record that matched.</summary>
        public LineRecord Record { get; }

        /// <summary>Which layer matched.</summary>
        public LineMatchLayer Layer { get; }

        /// <summary>
        /// True when the text on screen differs from the text the record was made
        /// from (line ID or fuzzy match): the mod's data for it may be out of date
        /// and is worth a look by a human.
        /// </summary>
        public bool NeedsReview { get; }
    }

    /// <summary>
    /// Finds a mod's record for a line of dialogue even after a game update
    /// changed the line: by line ID first, then exact text, then text that
    /// differs only in punctuation or case, then the closest text in the same
    /// node. A match by anything but the exact text is flagged for review.
    /// Records that could be more than one line are never guessed.
    /// </summary>
    /// <remarks>
    /// Experimental (Dialogue 1.1); see docs/STABLE_LINE_KEYS.md. Add records
    /// from Awake and keep the resolver for the life of the mod; Resolve is
    /// cheap enough to call for every line shown.
    /// </remarks>
    public sealed class LineResolver
    {
        private readonly Dictionary<string, LineRecord> _byLineId = new Dictionary<string, LineRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, LineRecord> _byHash = new Dictionary<string, LineRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<LineRecord>> _byNormalized = new Dictionary<string, List<LineRecord>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<LineRecord>> _byNode = new Dictionary<string, List<LineRecord>>(StringComparer.Ordinal);
        private readonly List<LineRecord> _all = new List<LineRecord>();

        /// <summary>Number of records added.</summary>
        public int Count => _all.Count;

        /// <summary>Adds a record. A later record with the same line ID or hash replaces the earlier one for that key.</summary>
        public void Add(LineRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            _all.Add(record);
            if (!string.IsNullOrEmpty(record.LineId))
            {
                _byLineId[record.LineId] = record;
            }
            if (!string.IsNullOrEmpty(record.Hash))
            {
                _byHash[record.Hash] = record;
            }
            if (!string.IsNullOrEmpty(record.NormalizedHash))
            {
                Append(_byNormalized, record.NormalizedHash, record);
            }
            if (!string.IsNullOrEmpty(record.Node))
            {
                Append(_byNode, record.Node, record);
            }
        }

        /// <summary>Makes a record from text the mod saw on screen, computing every key.</summary>
        public static LineRecord RecordFor(string displayedText, string lineId = null, string node = null, string speaker = null, object payload = null)
        {
            string normalized = LineKey.Normalize(displayedText);
            return new LineRecord
            {
                LineId = lineId,
                Hash = LineKey.Hash(displayedText),
                NormalizedHash = LineKey.Hash(normalized),
                Fingerprint = LineKey.Fingerprint(displayedText),
                NormalizedLength = normalized.Length,
                Node = node,
                Speaker = speaker,
                Payload = payload,
            };
        }

        /// <summary>Removes every record.</summary>
        public void Clear()
        {
            _byLineId.Clear();
            _byHash.Clear();
            _byNormalized.Clear();
            _byNode.Clear();
            _all.Clear();
        }

        /// <summary>
        /// Finds the record for <paramref name="line"/> as it is about to be shown
        /// with <paramref name="displayedText"/> (the text the component receives,
        /// which is what records are keyed on). Returns null when nothing matches
        /// or more than one record could.
        /// </summary>
        public LineMatch Resolve(DialogueLine line, string displayedText)
        {
            return Resolve(line?.LineId, line?.Node, line?.Speaker, displayedText);
        }

        /// <summary>The same lookup from bare values, for tools and tests.</summary>
        public LineMatch Resolve(string lineId, string node, string speaker, string displayedText)
        {
            if (!string.IsNullOrEmpty(lineId) && _byLineId.TryGetValue(lineId, out LineRecord byId))
            {
                bool sameText = displayedText != null && byId.Hash == LineKey.Hash(displayedText);
                return new LineMatch(byId, LineMatchLayer.LineId, needsReview: !sameText);
            }
            if (string.IsNullOrEmpty(displayedText))
            {
                return null;
            }
            if (_byHash.TryGetValue(LineKey.Hash(displayedText), out LineRecord byHash))
            {
                return new LineMatch(byHash, LineMatchLayer.Hash, needsReview: false);
            }
            string normalized = LineKey.Normalize(displayedText);
            if (_byNormalized.TryGetValue(LineKey.Hash(normalized), out List<LineRecord> same) && same.Count == 1)
            {
                return new LineMatch(same[0], LineMatchLayer.Normalized, needsReview: false);
            }
            return Fuzzy(node, speaker, normalized);
        }

        private LineMatch Fuzzy(string node, string speaker, string normalized)
        {
            if (normalized.Length < LineKey.MinFuzzyLength)
            {
                return null;
            }
            // Only lines of the same node are candidates; without a node, any line.
            // A node with no records gives no match rather than a guess elsewhere.
            List<LineRecord> pool;
            if (string.IsNullOrEmpty(node))
            {
                pool = _all;
            }
            else if (!_byNode.TryGetValue(node, out pool))
            {
                return null;
            }
            ulong fingerprint = LineKey.Fingerprint(normalized);
            int best = LineKey.MaxFuzzyDistance + 1;
            var tied = new List<LineRecord>();
            foreach (LineRecord record in pool)
            {
                if (record.Fingerprint == 0 || record.NormalizedLength < LineKey.MinFuzzyLength)
                {
                    continue;
                }
                int d = LineKey.Distance(fingerprint, record.Fingerprint);
                if (d < best)
                {
                    best = d;
                    tied.Clear();
                    tied.Add(record);
                }
                else if (d == best)
                {
                    tied.Add(record);
                }
            }
            if (tied.Count > 1 && !string.IsNullOrEmpty(speaker))
            {
                tied.RemoveAll(r => !string.Equals(r.Speaker, speaker, StringComparison.Ordinal));
            }
            if (tied.Count != 1)
            {
                return null;
            }
            return new LineMatch(tied[0], LineMatchLayer.Fuzzy, needsReview: true);
        }

        private static void Append(Dictionary<string, List<LineRecord>> map, string key, LineRecord record)
        {
            if (!map.TryGetValue(key, out List<LineRecord> list))
            {
                list = new List<LineRecord>();
                map[key] = list;
            }
            list.Add(record);
        }
    }
}
