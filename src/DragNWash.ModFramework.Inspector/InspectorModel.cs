using DragNWash.ModFramework.ToolWindow;
using TW = global::DragNWash.ModFramework.ToolWindow.ToolWindow;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework.Inspector
{
    // The Inspector tab's data: the hierarchy, a component's members through
    // reflection, a material's shader properties, and how a value is shown and
    // parsed. Nothing here draws; InspectorTab does. Nothing here persists;
    // an edit lives until the scene reloads or the game quits.
    internal static class InspectorModel
    {
        // ---- hierarchy ---------------------------------------------------------

        internal sealed class Node
        {
            public Transform Transform;
            public string Name;
            public int Depth;
            public bool HasChildren;
            public bool Active;
            public string Path;      // set for search results
            public string Scene;     // set for scene headings (Transform == null)
        }

        // Every loaded scene's roots plus the objects Unity keeps between
        // scenes, as a flat list in tree order, children only where expanded.
        internal static List<Node> BuildTree(HashSet<int> expanded)
        {
            var nodes = new List<Node>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                nodes.Add(new Node { Scene = scene.name, Name = scene.name, Depth = 0 });
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    AddSubtree(nodes, root.transform, 1, expanded);
                }
            }
            var kept = new List<Transform>();
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t != null && t.parent == null && t.gameObject.scene.IsValid() && t.gameObject.scene.name == "DontDestroyOnLoad" && t.hideFlags == HideFlags.None)
                {
                    kept.Add(t);
                }
            }
            if (kept.Count > 0)
            {
                kept.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                nodes.Add(new Node { Scene = "DontDestroyOnLoad", Name = "DontDestroyOnLoad", Depth = 0 });
                foreach (Transform t in kept)
                {
                    AddSubtree(nodes, t, 1, expanded);
                }
            }
            return nodes;
        }

        private static void AddSubtree(List<Node> nodes, Transform t, int depth, HashSet<int> expanded)
        {
            nodes.Add(new Node { Transform = t, Name = t.name, Depth = depth, HasChildren = t.childCount > 0, Active = t.gameObject.activeInHierarchy });
            if (t.childCount > 0 && expanded.Contains(t.GetInstanceID()))
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    AddSubtree(nodes, t.GetChild(i), depth + 1, expanded);
                }
            }
        }

        // Every object in a loaded scene whose name contains the text, with its path.
        internal static List<Node> Search(string text, int max = 500)
        {
            var found = new List<Node>();
            if (string.IsNullOrEmpty(text))
            {
                return found;
            }
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !t.gameObject.scene.IsValid() || t.hideFlags != HideFlags.None)
                {
                    continue;
                }
                if (t.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found.Add(new Node { Transform = t, Name = t.name, Depth = 0, Path = PathOf(t), Active = t.gameObject.activeInHierarchy });
                    if (found.Count >= max)
                    {
                        break;
                    }
                }
            }
            found.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        internal static string PathOf(Transform t)
        {
            if (t == null)
            {
                return "";
            }
            var parts = new List<string>();
            for (Transform p = t; p != null; p = p.parent)
            {
                parts.Add(p.name);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        // The object at a path ("Root/Child/Leaf"); with no '/', the first
        // object of that name in any loaded scene.
        internal static GameObject Find(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            if (path.IndexOf('/') < 0)
            {
                foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (t != null && t.gameObject.scene.IsValid() && t.hideFlags == HideFlags.None && t.name.Equals(path, StringComparison.OrdinalIgnoreCase))
                    {
                        return t.gameObject;
                    }
                }
                return null;
            }
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t != null && t.gameObject.scene.IsValid() && t.hideFlags == HideFlags.None && PathOf(t).Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    return t.gameObject;
                }
            }
            return null;
        }

        internal static void ExpandTo(Transform t, HashSet<int> expanded)
        {
            for (Transform p = t != null ? t.parent : null; p != null; p = p.parent)
            {
                expanded.Add(p.GetInstanceID());
            }
        }

        // ---- members -----------------------------------------------------------

        internal sealed class Member
        {
            public string Name;
            public Type Type;
            public bool IsPrivate;
            public bool CanWrite;
            public Func<object, object> Get;
            public Action<object, object> Set;
            public string Failure;   // the last exception from Get or Set, shown on the row
        }

        private static readonly Dictionary<Type, List<Member>> MemberCache = new Dictionary<Type, List<Member>>();

        // Fields and properties of a type, public first; those Unity hides
        // ([HideInInspector]) and the ones every Component carries are left out.
        internal static List<Member> MembersOf(Type type)
        {
            lock (MemberCache)
            {
                if (MemberCache.TryGetValue(type, out List<Member> cached))
                {
                    return cached;
                }
            }
            var members = new List<Member>();
            var seen = new HashSet<string>();
            const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (bool isPrivate in new[] { false, true })
            {
                BindingFlags flags = isPrivate ? Private : Public;
                for (Type t = type; t != null && t != typeof(object) && t != typeof(Component) && t != typeof(Behaviour) && t != typeof(MonoBehaviour) && t != typeof(UnityEngine.Object); t = t.BaseType)
                {
                    foreach (FieldInfo f in t.GetFields(flags | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsDefined(typeof(HideInInspector), true) || f.IsDefined(typeof(ObsoleteAttribute), true) || f.Name.EndsWith("k__BackingField") || !seen.Add(f.Name))
                        {
                            continue;
                        }
                        FieldInfo field = f;
                        members.Add(new Member
                        {
                            Name = f.Name, Type = f.FieldType, IsPrivate = isPrivate, CanWrite = !f.IsInitOnly && !f.IsLiteral,
                            Get = o => field.GetValue(o), Set = (o, v) => field.SetValue(o, v),
                        });
                    }
                    foreach (PropertyInfo p in t.GetProperties(flags | BindingFlags.DeclaredOnly))
                    {
                        if (p.GetIndexParameters().Length > 0 || !p.CanRead || p.IsDefined(typeof(ObsoleteAttribute), true) || p.IsDefined(typeof(HideInInspector), true) || !seen.Add(p.Name))
                        {
                            continue;
                        }
                        MethodInfo getter = p.GetGetMethod(true);
                        if (getter == null || getter.IsStatic)
                        {
                            continue;
                        }
                        PropertyInfo prop = p;
                        MethodInfo setter = p.GetSetMethod(true);
                        members.Add(new Member
                        {
                            Name = p.Name, Type = p.PropertyType, IsPrivate = isPrivate || !getter.IsPublic, CanWrite = setter != null && !setter.IsStatic,
                            Get = o => prop.GetValue(o, null), Set = (o, v) => prop.SetValue(o, v, null),
                        });
                    }
                }
            }
            members.Sort((a, b) => a.IsPrivate != b.IsPrivate ? a.IsPrivate.CompareTo(b.IsPrivate) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            lock (MemberCache)
            {
                MemberCache[type] = members;
            }
            return members;
        }

        // The members every component shows first, so the common ones are
        // not lost in the middle of the alphabet.
        internal static List<Member> GameObjectMembers()
        {
            return new List<Member>
            {
                new Member { Name = "name", Type = typeof(string), CanWrite = true, Get = o => ((GameObject)o).name, Set = (o, v) => ((GameObject)o).name = (string)v },
                new Member { Name = "active", Type = typeof(bool), CanWrite = true, Get = o => ((GameObject)o).activeSelf, Set = (o, v) => ((GameObject)o).SetActive((bool)v) },
                new Member { Name = "tag", Type = typeof(string), CanWrite = true, Get = o => ((GameObject)o).tag, Set = (o, v) => ((GameObject)o).tag = (string)v },
                new Member { Name = "layer", Type = typeof(int), CanWrite = true, Get = o => ((GameObject)o).layer, Set = (o, v) => ((GameObject)o).layer = (int)v },
                new Member { Name = "isStatic", Type = typeof(bool), CanWrite = true, Get = o => ((GameObject)o).isStatic, Set = (o, v) => ((GameObject)o).isStatic = (bool)v },
            };
        }

        // ---- materials -----------------------------------------------------------

        // A material's shader properties as members, asked from the shader itself.
        internal static List<Member> MaterialMembers(Material m)
        {
            var members = new List<Member>();
            if (m == null || m.shader == null)
            {
                return members;
            }
            Shader shader = m.shader;
            members.Add(new Member { Name = "shader", Type = typeof(Shader), Get = o => ((Material)o).shader });
            members.Add(new Member { Name = "renderQueue", Type = typeof(int), CanWrite = true, Get = o => ((Material)o).renderQueue, Set = (o, v) => ((Material)o).renderQueue = (int)v });
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                ShaderPropertyType type = shader.GetPropertyType(i);
                bool hidden = (shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0;
                if (hidden)
                {
                    continue;
                }
                string n = name;
                switch (type)
                {
                    case ShaderPropertyType.Float:
                        members.Add(new Member { Name = name, Type = typeof(float), CanWrite = true, Get = o => ((Material)o).GetFloat(n), Set = (o, v) => ((Material)o).SetFloat(n, (float)v) });
                        break;
                    case ShaderPropertyType.Range:
                    {
                        Vector2 limits = shader.GetPropertyRangeLimits(i);
                        members.Add(new Member { Name = $"{name}  [{Fmt(limits.x)} .. {Fmt(limits.y)}]", Type = typeof(float), CanWrite = true, Get = o => ((Material)o).GetFloat(n), Set = (o, v) => ((Material)o).SetFloat(n, Mathf.Clamp((float)v, limits.x, limits.y)) });
                        break;
                    }
                    case ShaderPropertyType.Int:
                        members.Add(new Member { Name = name, Type = typeof(int), CanWrite = true, Get = o => ((Material)o).GetInt(n), Set = (o, v) => ((Material)o).SetInt(n, (int)v) });
                        break;
                    case ShaderPropertyType.Color:
                        members.Add(new Member { Name = name, Type = typeof(Color), CanWrite = true, Get = o => ((Material)o).GetColor(n), Set = (o, v) => ((Material)o).SetColor(n, (Color)v) });
                        break;
                    case ShaderPropertyType.Vector:
                        members.Add(new Member { Name = name, Type = typeof(Vector4), CanWrite = true, Get = o => ((Material)o).GetVector(n), Set = (o, v) => ((Material)o).SetVector(n, (Vector4)v) });
                        break;
                    case ShaderPropertyType.Texture:
                        members.Add(new Member { Name = name, Type = typeof(Texture), Get = o => ((Material)o).GetTexture(n) });
                        break;
                }
            }
            foreach (string keyword in m.shaderKeywords)
            {
                string k = keyword;
                members.Add(new Member { Name = "keyword " + keyword, Type = typeof(bool), CanWrite = true, Get = o => ((Material)o).IsKeywordEnabled(k), Set = (o, v) => { if ((bool)v) ((Material)o).EnableKeyword(k); else ((Material)o).DisableKeyword(k); } });
            }
            return members;
        }

        // How many renderers draw with this material: they all change together.
        internal static int RendererCount(Material m)
        {
            int n = 0;
            foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (r == null || !r.gameObject.scene.IsValid())
                {
                    continue;
                }
                foreach (Material shared in r.sharedMaterials)
                {
                    if (shared == m)
                    {
                        n++;
                        break;
                    }
                }
            }
            return n;
        }

        // ---- values --------------------------------------------------------------

        // True for the types a row can edit through a text field or a toggle.
        internal static bool IsEditableType(Type t)
        {
            if (t == null) return false;
            if (t.IsEnum) return true;
            return t == typeof(bool) || t == typeof(string) || IsNumber(t)
                || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) || t == typeof(Quaternion)
                || t == typeof(Color) || t == typeof(Color32) || t == typeof(Rect) || t == typeof(Bounds)
                || t == typeof(Vector2Int) || t == typeof(Vector3Int);
        }

        internal static bool IsNumber(Type t)
        {
            return t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte) || t == typeof(decimal);
        }

        internal static bool IsList(Type t)
        {
            return t != typeof(string) && (t.IsArray || typeof(IList).IsAssignableFrom(t));
        }

        internal static string Fmt(float f)
        {
            return f.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // The value as the row shows it, and as the text field starts with.
        internal static string Format(object v)
        {
            if (v == null) return "null";
            switch (v)
            {
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case float f: return Fmt(f);
                case double d: return d.ToString("0.###", CultureInfo.InvariantCulture);
                case Vector2 a: return $"{Fmt(a.x)}, {Fmt(a.y)}";
                case Vector3 a: return $"{Fmt(a.x)}, {Fmt(a.y)}, {Fmt(a.z)}";
                case Vector4 a: return $"{Fmt(a.x)}, {Fmt(a.y)}, {Fmt(a.z)}, {Fmt(a.w)}";
                case Vector2Int a: return $"{a.x}, {a.y}";
                case Vector3Int a: return $"{a.x}, {a.y}, {a.z}";
                case Quaternion q: { Vector3 e = q.eulerAngles; return $"{Fmt(e.x)}, {Fmt(e.y)}, {Fmt(e.z)}"; }
                case Color c: return $"{Fmt(c.r)}, {Fmt(c.g)}, {Fmt(c.b)}, {Fmt(c.a)}";
                case Color32 c: return $"{c.r}, {c.g}, {c.b}, {c.a}";
                case Rect r: return $"{Fmt(r.x)}, {Fmt(r.y)}, {Fmt(r.width)}, {Fmt(r.height)}";
                case Bounds b: return $"{Fmt(b.center.x)}, {Fmt(b.center.y)}, {Fmt(b.center.z)}, {Fmt(b.size.x)}, {Fmt(b.size.y)}, {Fmt(b.size.z)}";
                case UnityEngine.Object o: return o == null ? "null (destroyed)" : $"{o.name} ({o.GetType().Name})";
                case IList list: return $"{list.Count} item(s)";
            }
            if (v is IFormattable f2)
            {
                return f2.ToString(null, CultureInfo.InvariantCulture);
            }
            string text = v.ToString();
            return text.Length > 200 ? text.Substring(0, 200) + "..." : text;
        }

        // The text typed in a row back into the member's type, or an error.
        internal static object Parse(Type t, string text, out string error)
        {
            error = null;
            text = text ?? "";
            try
            {
                if (t == typeof(string)) return text;
                if (t == typeof(bool))
                {
                    string b = text.Trim().ToLowerInvariant();
                    if (b == "true" || b == "1" || b == "on" || b == "yes") return true;
                    if (b == "false" || b == "0" || b == "off" || b == "no") return false;
                    error = "true or false"; return null;
                }
                if (t.IsEnum)
                {
                    try { return Enum.Parse(t, text.Trim(), true); }
                    catch { error = "one of " + string.Join(", ", Enum.GetNames(t)); return null; }
                }
                if (IsNumber(t))
                {
                    return Convert.ChangeType(text.Trim(), t, CultureInfo.InvariantCulture);
                }
                float[] n = Numbers(text);
                if (t == typeof(Vector2)) return Need(n, 2, out error) ? new Vector2(n[0], n[1]) : (object)null;
                if (t == typeof(Vector3)) return Need(n, 3, out error) ? new Vector3(n[0], n[1], n[2]) : (object)null;
                if (t == typeof(Vector4)) return Need(n, 4, out error) ? new Vector4(n[0], n[1], n[2], n[3]) : (object)null;
                if (t == typeof(Vector2Int)) return Need(n, 2, out error) ? new Vector2Int((int)n[0], (int)n[1]) : (object)null;
                if (t == typeof(Vector3Int)) return Need(n, 3, out error) ? new Vector3Int((int)n[0], (int)n[1], (int)n[2]) : (object)null;
                if (t == typeof(Quaternion)) return Need(n, 3, out error) ? Quaternion.Euler(n[0], n[1], n[2]) : (object)null;
                if (t == typeof(Color))
                {
                    if (n.Length == 3) return new Color(n[0], n[1], n[2], 1f);
                    return Need(n, 4, out error) ? new Color(n[0], n[1], n[2], n[3]) : (object)null;
                }
                if (t == typeof(Color32))
                {
                    if (n.Length == 3) return new Color32((byte)n[0], (byte)n[1], (byte)n[2], 255);
                    return Need(n, 4, out error) ? new Color32((byte)n[0], (byte)n[1], (byte)n[2], (byte)n[3]) : (object)null;
                }
                if (t == typeof(Rect)) return Need(n, 4, out error) ? new Rect(n[0], n[1], n[2], n[3]) : (object)null;
                if (t == typeof(Bounds)) return Need(n, 6, out error) ? new Bounds(new Vector3(n[0], n[1], n[2]), new Vector3(n[3], n[4], n[5])) : (object)null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
            error = "not an editable type";
            return null;
        }

        private static bool Need(float[] n, int count, out string error)
        {
            error = n.Length == count ? null : $"{count} numbers separated by commas";
            return error == null;
        }

        private static float[] Numbers(string text)
        {
            var list = new List<float>();
            foreach (string part in text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                {
                    return new float[0];
                }
                list.Add(f);
            }
            return list.ToArray();
        }

        // ---- composite values: one field per component ------------------------------

        internal static bool IsComposite(Type t)
        {
            return t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) || t == typeof(Quaternion)
                || t == typeof(Vector2Int) || t == typeof(Vector3Int) || t == typeof(Color) || t == typeof(Color32)
                || t == typeof(Rect) || t == typeof(Bounds);
        }

        internal static string[] ComponentLabels(Type t)
        {
            if (t == typeof(Vector2) || t == typeof(Vector2Int)) return new[] { "x", "y" };
            if (t == typeof(Vector3) || t == typeof(Vector3Int) || t == typeof(Quaternion)) return new[] { "x", "y", "z" };
            if (t == typeof(Vector4)) return new[] { "x", "y", "z", "w" };
            if (t == typeof(Color) || t == typeof(Color32)) return new[] { "r", "g", "b", "a" };
            if (t == typeof(Rect)) return new[] { "x", "y", "w", "h" };
            if (t == typeof(Bounds)) return new[] { "cx", "cy", "cz", "sx", "sy", "sz" };
            return new string[0];
        }

        internal static float[] Components(object v)
        {
            switch (v)
            {
                case Vector2 a: return new[] { a.x, a.y };
                case Vector3 a: return new[] { a.x, a.y, a.z };
                case Vector4 a: return new[] { a.x, a.y, a.z, a.w };
                case Vector2Int a: return new float[] { a.x, a.y };
                case Vector3Int a: return new float[] { a.x, a.y, a.z };
                case Quaternion q: { Vector3 e = q.eulerAngles; return new[] { e.x, e.y, e.z }; }
                case Color c: return new[] { c.r, c.g, c.b, c.a };
                case Color32 c: return new float[] { c.r, c.g, c.b, c.a };
                case Rect r: return new[] { r.x, r.y, r.width, r.height };
                case Bounds b: return new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z };
            }
            return new float[0];
        }

        internal static object Compose(Type t, float[] n)
        {
            if (t == typeof(Vector2)) return new Vector2(n[0], n[1]);
            if (t == typeof(Vector3)) return new Vector3(n[0], n[1], n[2]);
            if (t == typeof(Vector4)) return new Vector4(n[0], n[1], n[2], n[3]);
            if (t == typeof(Vector2Int)) return new Vector2Int((int)n[0], (int)n[1]);
            if (t == typeof(Vector3Int)) return new Vector3Int((int)n[0], (int)n[1], (int)n[2]);
            if (t == typeof(Quaternion)) return Quaternion.Euler(n[0], n[1], n[2]);
            if (t == typeof(Color)) return new Color(n[0], n[1], n[2], n[3]);
            if (t == typeof(Color32)) return new Color32((byte)n[0], (byte)n[1], (byte)n[2], (byte)n[3]);
            if (t == typeof(Rect)) return new Rect(n[0], n[1], n[2], n[3]);
            if (t == typeof(Bounds)) return new Bounds(new Vector3(n[0], n[1], n[2]), new Vector3(n[3], n[4], n[5]));
            return null;
        }

        // Cycle an enum to its next value (the row's button for enums).
        internal static object NextEnum(Type t, object current)
        {
            Array values = Enum.GetValues(t);
            if (values.Length == 0) return current;
            int index = Array.IndexOf(values, current);
            return values.GetValue((index + 1) % values.Length);
        }

        internal static string TypeName(Type t)
        {
            if (t == null) return "?";
            if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
            if (t.IsGenericType)
            {
                var sb = new StringBuilder(t.Name.Split('`')[0]).Append('<');
                Type[] args = t.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(TypeName(args[i]));
                }
                return sb.Append('>').ToString();
            }
            if (t == typeof(int)) return "int";
            if (t == typeof(float)) return "float";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(double)) return "double";
            if (t == typeof(long)) return "long";
            return t.Name;
        }
    }
}
