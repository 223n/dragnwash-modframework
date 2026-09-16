using System;
using System.Security.Cryptography;
using System.Text;

namespace DragNWash.ModFramework.Dialogue
{
    /// <summary>
    /// Keys that identify a line of dialogue without carrying its text, so a mod
    /// can keep data about a line (a translation, a bookmark) in a repository
    /// that ships none of the game's script, and find the line again after a
    /// game update edits it. Every function here is deterministic and has the
    /// same definition in <c>tools/linekeys.py</c>, which offline tools use.
    /// </summary>
    /// <remarks>
    /// Experimental (Dialogue 1.1). The definitions may still change before a
    /// release; see docs/STABLE_LINE_KEYS.md.
    /// </remarks>
    public static class LineKey
    {
        /// <summary>Length of <see cref="Hash"/> and <see cref="NormalizedHash"/>: 16 hex digits, 64 bits.</summary>
        public const int HashLength = 16;

        /// <summary>Normalized texts shorter than this are too short for <see cref="Fingerprint"/> matching.</summary>
        public const int MinFuzzyLength = 12;

        /// <summary>Largest <see cref="Distance"/> between fingerprints that still counts as the same line.</summary>
        public const int MaxFuzzyDistance = 10;

        [ThreadStatic] private static SHA256 _sha;

        /// <summary>
        /// The exact-text key: the first 16 hex digits of SHA-256 over the UTF-8
        /// bytes of <paramref name="text"/> as given, tags included. Changes with
        /// any edit to the text.
        /// </summary>
        public static string Hash(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }
            if (_sha == null)
            {
                _sha = SHA256.Create();
            }
            byte[] digest = _sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(HashLength);
            for (int i = 0; i < HashLength / 2; i++)
            {
                sb.Append(digest[i].ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// The text with rich-text tags removed, ASCII letters lowercased,
        /// everything but letters, digits and spaces dropped, and runs of
        /// whitespace collapsed to one space. Survives edits to punctuation,
        /// capitalisation, spacing and TextMeshPro tags.
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var sb = new StringBuilder(text.Length);
            bool inTag = false, pendingSpace = false;
            foreach (char c in text)
            {
                if (inTag)
                {
                    if (c == '>')
                    {
                        inTag = false;
                    }
                    continue;
                }
                if (c == '<')
                {
                    inTag = true;
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = true;
                    continue;
                }
                if (!char.IsLetterOrDigit(c))
                {
                    continue;
                }
                if (pendingSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }
                pendingSpace = false;
                sb.Append(c >= 'A' && c <= 'Z' ? (char)(c + 32) : c);
            }
            return sb.ToString();
        }

        /// <summary><see cref="Hash"/> of <see cref="Normalize"/>.</summary>
        public static string NormalizedHash(string text)
        {
            return Hash(Normalize(text));
        }

        /// <summary>
        /// A 64-bit SimHash of the normalized text: every 3-character window is
        /// hashed with FNV-1a and votes on each bit. Similar texts get
        /// fingerprints a few bits apart; see <see cref="Distance"/>.
        /// </summary>
        public static ulong Fingerprint(string text)
        {
            string n = Normalize(text);
            if (n.Length == 0)
            {
                return 0;
            }
            var votes = new int[64];
            int windows = n.Length < 3 ? 1 : n.Length - 2;
            for (int i = 0; i < windows; i++)
            {
                ulong h = Fnv1a64(n.Length < 3 ? n : n.Substring(i, 3));
                for (int b = 0; b < 64; b++)
                {
                    votes[b] += ((h >> b) & 1UL) != 0 ? 1 : -1;
                }
            }
            ulong result = 0;
            for (int b = 0; b < 64; b++)
            {
                if (votes[b] > 0)
                {
                    result |= 1UL << b;
                }
            }
            return result;
        }

        /// <summary><see cref="Fingerprint"/> as 16 lowercase hex digits, the form stored in files.</summary>
        public static string FingerprintText(string text)
        {
            return Fingerprint(text).ToString("x16");
        }

        /// <summary>Parses a fingerprint written by <see cref="FingerprintText"/>.</summary>
        public static bool TryParseFingerprint(string hex, out ulong fingerprint)
        {
            fingerprint = 0;
            return hex != null && hex.Length == 16 &&
                   ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out fingerprint);
        }

        /// <summary>Number of bits that differ between two fingerprints (0 = identical normalized text, most likely).</summary>
        public static int Distance(ulong a, ulong b)
        {
            ulong x = a ^ b;
            int count = 0;
            while (x != 0)
            {
                x &= x - 1;
                count++;
            }
            return count;
        }

        private static ulong Fnv1a64(string s)
        {
            const ulong Offset = 14695981039346656037UL, Prime = 1099511628211UL;
            ulong h = Offset;
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                h ^= b;
                h = unchecked(h * Prime);
            }
            return h;
        }
    }
}
