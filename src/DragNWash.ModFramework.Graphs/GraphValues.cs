using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DragNWash.ModFramework.Graphs
{
    // The values a graph works with are the ones operations return: null, text,
    // numbers, true/false, lists and objects. These are the few rules that say
    // how they compare, count as true and read as text, in one place so the
    // checker, the runner and the Python sketch (tools/graphs.py) agree.
    internal static class GraphValues
    {
        internal static bool IsNumber(object v) => v is double || v is float || v is int || v is long || v is short || v is byte;

        internal static double Number(object v) => Convert.ToDouble(v, CultureInfo.InvariantCulture);

        /// <summary>False, 0, "", null and an empty list or object count as false.</summary>
        internal static bool Truthy(object v)
        {
            switch (v)
            {
                case null: return false;
                case bool b: return b;
                case string s: return s.Length > 0;
            }
            if (IsNumber(v)) return Number(v) != 0;
            if (v is ICollection c) return c.Count > 0;
            if (v is IEnumerable e)
            {
                foreach (object unused in e) return true;
                return false;
            }
            return true;
        }

        /// <summary>A value as text: lists and objects as JSON, as "join" writes them.</summary>
        internal static string Text(object v)
        {
            switch (v)
            {
                case null: return "";
                case string s: return s;
                case bool b: return b ? "true" : "false";
            }
            if (IsNumber(v))
            {
                double d = Number(v);
                // A whole number reads as one: "3 flags", not "3.0 flags".
                return d == Math.Floor(d) && !double.IsInfinity(d)
                    ? ((long)d).ToString(CultureInfo.InvariantCulture)
                    : d.ToString("R", CultureInfo.InvariantCulture);
            }
            if (v is IDictionary || v is IEnumerable)
            {
                var sb = new StringBuilder();
                Json(sb, v);
                return sb.ToString();
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>How long text, a list or an object is; 0 for anything else.</summary>
        internal static int Length(object v)
        {
            switch (v)
            {
                case null: return 0;
                case string s: return s.Length;
                case ICollection c: return c.Count;
            }
            if (v is IEnumerable e)
            {
                int n = 0;
                foreach (object unused in e) n++;
                return n;
            }
            return 0;
        }

        /// <summary>Equal: numbers as numbers, anything else as text.</summary>
        internal static bool Same(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b)) return Number(a) == Number(b);
            if (a is bool x && b is bool y) return x == y;
            return string.Equals(Text(a), Text(b), StringComparison.Ordinal);
        }

        /// <summary>Less than and its family: numbers as numbers, anything else as text.</summary>
        internal static int Compare(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b)) return Number(a).CompareTo(Number(b));
            return string.CompareOrdinal(Text(a), Text(b));
        }

        /// <summary>Text in text (ignoring case), or an item in a list.</summary>
        internal static bool Contains(object a, object b)
        {
            if (a is IEnumerable list && !(a is string))
            {
                foreach (object item in list)
                {
                    if (Same(item, b)) return true;
                }
                return false;
            }
            return Text(a).IndexOf(Text(b), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>A field of an object, or an item of a list (0 first, -1 last); null when it is not there.</summary>
        internal static object Get(object value, object key)
        {
            if (value is IDictionary<string, object> o)
            {
                return key is string name && o.TryGetValue(name, out object found) ? found : null;
            }
            if (value is IDictionary plain)
            {
                return key is string name && plain.Contains(name) ? plain[name] : null;
            }
            if (value is IList list && IsNumber(key))
            {
                int at = (int)Number(key);
                if (at < 0) at += list.Count;
                return at >= 0 && at < list.Count ? list[at] : null;
            }
            return null;
        }

        // Only what a graph can hold, so this stays small; the registry's own
        // writer (Operations.ToJson) is in the core, which the checker and the
        // runner are kept free of so they can be tested without the game.
        private static void Json(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: Quote(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
            }
            if (IsNumber(v))
            {
                sb.Append(Text(v));
                return;
            }
            if (v is IDictionary<string, object> o)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in o)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Quote(sb, kv.Key);
                    sb.Append(':');
                    Json(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            if (v is IEnumerable list)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Json(sb, item);
                }
                sb.Append(']');
                return;
            }
            Quote(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        private static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
