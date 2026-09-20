using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// A small JSON reader for the files data mods are made of (mod.json, the
    /// overrides and graphs beside it): objects become
    /// <see cref="Dictionary{String, Object}"/>, arrays <see cref="List{Object}"/>,
    /// numbers <see cref="double"/>, and strings, true, false and null
    /// themselves. Unity's <c>JsonUtility</c> left a list of objects empty in
    /// the game, so the files are read here instead;
    /// <see cref="Operations.ToJson"/> writes JSON. Since 1.4.0.
    /// </summary>
    public static class Json
    {
        /// <summary>Reads a JSON text. Throws <see cref="FormatException"/> when it is not JSON.</summary>
        public static object Parse(string text)
        {
            var reader = new Reader(text ?? "");
            reader.Space();
            object value = reader.Value();
            reader.Space();
            if (!reader.End) throw reader.Error("text after the end");
            return value;
        }

        /// <summary>The value of a key as text, or null.</summary>
        public static string String(Dictionary<string, object> o, string key)
        {
            return o != null && o.TryGetValue(key, out object v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        /// <summary>The value of a key as a whole number, or 0.</summary>
        public static int Int(Dictionary<string, object> o, string key)
        {
            return o != null && o.TryGetValue(key, out object v) && v is double d ? (int)d : 0;
        }

        /// <summary>The value of a key as true or false, or false.</summary>
        public static bool Bool(Dictionary<string, object> o, string key)
        {
            return o != null && o.TryGetValue(key, out object v) && (v is bool b ? b : v is string s && s.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;
            private int _line = 1;

            internal Reader(string s) { _s = s; }

            internal bool End => _i >= _s.Length;

            internal FormatException Error(string what) => new FormatException($"line {_line}: {what}");

            internal void Space()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == '\n') { _line++; _i++; }
                    else if (c == ' ' || c == '\t' || c == '\r' || c == '﻿') _i++;
                    else break;
                }
            }

            internal object Value()
            {
                if (End) throw Error("unexpected end");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return Text();
                    case 't': Word("true"); return true;
                    case 'f': Word("false"); return false;
                    case 'n': Word("null"); return null;
                }
                if (c == '-' || char.IsDigit(c)) return Number();
                throw Error($"unexpected '{c}'");
            }

            private void Word(string w)
            {
                if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) throw Error("expected " + w);
                _i += w.Length;
            }

            private void Expect(char c)
            {
                Space();
                if (End || _s[_i] != c) throw Error($"expected '{c}'");
                _i++;
            }

            private Dictionary<string, object> Object()
            {
                var o = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++;
                Space();
                if (!End && _s[_i] == '}') { _i++; return o; }
                while (true)
                {
                    Space();
                    if (End || _s[_i] != '"') throw Error("expected a name in quotes");
                    string key = Text();
                    Expect(':');
                    Space();
                    o[key] = Value();
                    Space();
                    if (End) throw Error("unexpected end");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return o; }
                    throw Error("expected ',' or '}'");
                }
            }

            private List<object> Array()
            {
                var a = new List<object>();
                _i++;
                Space();
                if (!End && _s[_i] == ']') { _i++; return a; }
                while (true)
                {
                    Space();
                    a.Add(Value());
                    Space();
                    if (End) throw Error("unexpected end");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return a; }
                    throw Error("expected ',' or ']'");
                }
            }

            private string Text()
            {
                var sb = new StringBuilder();
                _i++;
                while (true)
                {
                    if (End) throw Error("a text without its closing quote");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\n') throw Error("a line break inside a text");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("unexpected end");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("a short \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw Error($"unknown escape \\{e}");
                    }
                }
            }

            private double Number()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw Error("not a number");
                return d;
            }
        }
    }
}
