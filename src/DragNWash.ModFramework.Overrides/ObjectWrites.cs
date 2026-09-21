using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework.Overrides
{
    // The write operations a graph may call: the three changes this library
    // already knows how to make and to put back, offered one at a time at a
    // moment the caller chooses, instead of whenever an object appears
    // (docs/GRAPHS.md, "The first writes graphs need"; decision 7 puts them
    // here, where the value parsing and the finding of objects already live).
    //
    // Each one says how to put itself back (OperationArgs.TakeBack), so the
    // Graphs library can undo a graph's changes when it is switched off,
    // reloaded, or fails three times, and the Inspector's History can list them
    // beside the edits made by hand. A write that could not be undone would not
    // be offered to graphs at all.
    //
    // None of them lasts beyond the session: nothing here writes a file or a
    // save (Operation.Lasting stays false, and lasting writes are not offered
    // to graphs in this version).
    internal static class ObjectWrites
    {
        internal static void Register()
        {
            string g = GameOverrides.Guid;

            Operations.Register(g, "objects.member.set",
                "Sets a field or property of a component on an object in a loaded scene, as one overrides row does. Put back when the caller is switched off.",
                OperationKind.Write, "{ path, component, member, before, after }", SetMember,
                Operations.Parameter("path", OperationType.String, "The object's path in the scene: Root/Child/Grandchild.", true),
                Operations.Parameter("component", OperationType.String, "The component's type name, as the Inspector shows it (Light, Rigidbody).", true),
                Operations.Parameter("member", OperationType.String, "The field or property to set.", true),
                Operations.Parameter("value", OperationType.String, "The value, written as the Inspector's rows show it (1.5, true, #RRGGBB, x,y,z).", true),
                Operations.Parameter("index", OperationType.Number, "Which component, when the object has more than one of that type (0 is the first)."),
                Operations.Parameter("private", OperationType.Boolean, "Set a private field or property (the game's scripts keep their settings in these)."),
                Operations.Parameter("scene", OperationType.String, "Only an object in this scene."));

            Operations.Register(g, "objects.material.set",
                "Sets a property of a material on an object's renderer, as one overrides row does. Put back when the caller is switched off.",
                OperationKind.Write, "{ path, material, property, before, after }", SetMaterial,
                Operations.Parameter("path", OperationType.String, "The object's path in the scene.", true),
                Operations.Parameter("material", OperationType.String, "The material's name, without the Instance suffix.", true),
                Operations.Parameter("property", OperationType.String, "The shader property to set (_BaseColor, _Smoothness).", true),
                Operations.Parameter("value", OperationType.String, "The value, as the Inspector's rows show it.", true),
                Operations.Parameter("component", OperationType.String, "The renderer's type name, when the object has more than one renderer."),
                Operations.Parameter("index", OperationType.Number, "Which renderer of that type (0 is the first)."),
                Operations.Parameter("scene", OperationType.String, "Only an object in this scene."));

            Operations.Register(g, "objects.active.set",
                "Shows or hides an object in a loaded scene. Put back when the caller is switched off.",
                OperationKind.Write, "{ path, before, after }", SetActive,
                Operations.Parameter("path", OperationType.String, "The object's path in the scene.", true),
                Operations.Parameter("active", OperationType.Boolean, "True shows it, false hides it.", true),
                Operations.Parameter("scene", OperationType.String, "Only an object in this scene."));

            Operations.Register(g, "objects.writes",
                "What this library has changed in the game this session and who asked for it, with the other mods that changed the same thing. Reading it changes nothing.",
                OperationKind.Read, "[{ target, by, also, value }]", args => WriteLedger.Report());
        }

        // ---- the three writes --------------------------------------------------

        private static object SetMember(OperationArgs args)
        {
            GameObject go = Find(args);
            string componentName = Need(args, "component");
            string memberName = Need(args, "member");
            string text = Need(args, "value");
            int index = args.Int("index");
            bool wantPrivate = args.Bool("private");

            Component c = FindComponent(go, componentName, index);
            if (c == null)
            {
                throw new InvalidOperationException($"{go.name} has no {componentName}{(index > 0 ? " #" + index : "")}.");
            }

            Member m = FindMember(c, memberName, wantPrivate);
            if (m == null)
            {
                throw new InvalidOperationException(
                    $"{c.GetType().Name} has no {(wantPrivate ? "" : "public ")}writable field or property {memberName}" +
                    (wantPrivate ? "." : " (a private one needs private: true)."));
            }
            if (!OverrideValues.Supports(m.Type))
            {
                throw new InvalidOperationException($"{memberName} is a {m.Type.Name}, which cannot be set this way.");
            }
            object value = OverrideValues.Deserialize(m.Type, text, out string error);
            if (error != null)
            {
                throw new InvalidOperationException($"\"{text}\" is not a {m.Type.Name}: {error}.");
            }

            object before = m.Get();
            m.Set(value);
            string label = $"{Path(go)} [{c.GetType().Name}] {memberName}";
            PutBack(args, WriteLedger.KeyOf(c, memberName), label, before, value, m.Get, m.Set, c);

            return new Dictionary<string, object>
            {
                ["path"] = Path(go),
                ["component"] = c.GetType().Name,
                ["member"] = memberName,
                ["before"] = Show(before),
                ["after"] = Show(value),
            };
        }

        private static object SetMaterial(OperationArgs args)
        {
            GameObject go = Find(args);
            string materialName = Need(args, "material");
            string property = Need(args, "property");
            string text = Need(args, "value");
            string componentName = args.String("component");
            int index = args.Int("index");

            Renderer r = componentName != null
                ? FindComponent(go, componentName, index) as Renderer
                : go.GetComponents<Renderer>().Skip(index).FirstOrDefault();
            if (r == null)
            {
                throw new InvalidOperationException($"{go.name} has no {componentName ?? "Renderer"}.");
            }
            Material material = r.sharedMaterials.FirstOrDefault(x => x != null && (x.name == materialName || x.name == materialName + " (Instance)"));
            if (material == null)
            {
                throw new InvalidOperationException($"{go.name}'s renderer has no material {materialName}.");
            }
            Shader shader = material.shader;
            if (shader == null || shader.FindPropertyIndex(property) < 0)
            {
                throw new InvalidOperationException($"{material.name}'s shader has no property {property}.");
            }

            MaterialProperty mp = MaterialProperty.For(material, shader, property);
            object value = OverrideValues.Deserialize(mp.Type, text, out string error);
            if (error != null)
            {
                throw new InvalidOperationException($"\"{text}\" is not a {mp.Type.Name}: {error}.");
            }

            object before = mp.Get();
            mp.Set(value);
            string label = $"{Path(go)} [{r.GetType().Name}] material {material.name} {property}";
            PutBack(args, WriteLedger.KeyOf(material, property), label, before, value, mp.Get, mp.Set, material);

            return new Dictionary<string, object>
            {
                ["path"] = Path(go),
                ["material"] = material.name,
                ["property"] = property,
                ["before"] = Show(before),
                ["after"] = Show(value),
            };
        }

        private static object SetActive(OperationArgs args)
        {
            GameObject go = Find(args);
            bool active = args.Bool("active");
            bool before = go.activeSelf;
            GameObject target = go;
            go.SetActive(active);
            PutBack(args, WriteLedger.KeyOf(go, "active"), $"{Path(go)} active", before, active,
                () => target.activeSelf, v => target.SetActive((bool)v), go);
            return new Dictionary<string, object>
            {
                ["path"] = Path(go),
                ["before"] = before,
                ["after"] = active,
            };
        }

        // ---- putting it back ---------------------------------------------------

        // Notes the write in the ledger (so two mods meeting on one member are
        // seen, here and on the Mods screen) and says how to undo it.
        //
        // Two things the undo looks at before it writes: the object may be gone
        // by then, and putting a value into a destroyed object throws; and
        // somebody else may have written the member since, in which case putting
        // our value back would quietly undo theirs. Either way it leaves the
        // value alone and says so, rather than failing the graph.
        private static void PutBack(OperationArgs args, string key, string label, object before, object after,
            Func<object> get, Action<object> set, UnityEngine.Object owner)
        {
            string caller = args.Caller;
            WriteLedger.Note note = WriteLedger.Record(key, label, caller, after, get, owner, out string other);
            if (other != null)
            {
                OverridesPlugin.Log.LogWarning(
                    $"[overrides] {other} and {caller} both change {label}; the value from {caller} is used (it wrote last).");
            }
            args.TakeBack(label, () =>
            {
                if (owner == null)
                {
                    WriteLedger.Forget(note);
                    return false;
                }
                if (!WriteLedger.StillOurs(note, caller))
                {
                    string since = WriteLedger.LastWriter(note);
                    if (since == null || since == caller)
                    {
                        // Our own earlier write, already put back by the take-back
                        // after it: nothing to say, and nobody to blame.
                        OverridesPlugin.Log.LogDebug($"[overrides] {label} is already back where it was.");
                    }
                    else
                    {
                        OverridesPlugin.Log.LogInfo(
                            $"[overrides] {label} was left as it is: {since} changed it after {caller} did.");
                    }
                    return false;
                }
                try
                {
                    set(before);
                }
                catch
                {
                    return false;
                }
                WriteLedger.Forget(note);
                return true;
            }, Show(before), Show(after));
        }

        // ---- finding things ----------------------------------------------------

        private static string Need(OperationArgs args, string name)
        {
            string value = args.String(name);
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException($"{name} is needed.");
            }
            return value;
        }

        // The same path an overrides row uses: Root/Child/Grandchild, in any
        // loaded scene unless one is named. The first match wins, so a path that
        // is not unique is the caller's problem, as it is for overrides.
        private static GameObject Find(OperationArgs args)
        {
            string path = Need(args, "path").Trim('/');
            string scene = args.String("scene");
            string root = path.Contains("/") ? path.Substring(0, path.IndexOf('/')) : path;
            string rest = path.Contains("/") ? path.Substring(path.IndexOf('/') + 1) : null;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded || (scene != null && !string.Equals(s.name, scene, StringComparison.Ordinal)))
                {
                    continue;
                }
                foreach (GameObject go in s.GetRootGameObjects())
                {
                    if (go == null || go.name != root)
                    {
                        continue;
                    }
                    Transform t = rest == null ? go.transform : go.transform.Find(rest);
                    if (t != null)
                    {
                        return t.gameObject;
                    }
                }
            }
            throw new InvalidOperationException($"No object at {path}{(scene != null ? " in " + scene : "")}.");
        }

        private static Component FindComponent(GameObject go, string name, int index)
        {
            int seen = 0;
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null)
                {
                    continue;
                }
                Type t = c.GetType();
                if (t.Name != name && t.FullName != name)
                {
                    continue;
                }
                if (seen++ == index)
                {
                    return c;
                }
            }
            return null;
        }

        private sealed class Member
        {
            internal Type Type;
            internal Func<object> Get;
            internal Action<object> Set;
        }

        private static Member FindMember(Component c, string name, bool wantPrivate)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | (wantPrivate ? BindingFlags.NonPublic : 0);
            for (Type t = c.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (f != null && !f.IsInitOnly && !f.IsLiteral)
                {
                    return new Member { Type = f.FieldType, Get = () => f.GetValue(c), Set = v => f.SetValue(c, v) };
                }
                PropertyInfo p = t.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                if (p != null && p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                    && (wantPrivate || (p.GetGetMethod() != null && p.GetSetMethod() != null)))
                {
                    return new Member { Type = p.PropertyType, Get = () => p.GetValue(c, null), Set = v => p.SetValue(c, v, null) };
                }
            }
            return null;
        }

        // A shader property, read and written by its kind, as the applier does.
        private sealed class MaterialProperty
        {
            internal Type Type;
            internal Func<object> Get;
            internal Action<object> Set;

            internal static MaterialProperty For(Material m, Shader shader, string property)
            {
                int index = shader.FindPropertyIndex(property);
                int id = Shader.PropertyToID(property);
                switch (shader.GetPropertyType(index))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        return new MaterialProperty { Type = typeof(Color), Get = () => m.GetColor(id), Set = v => m.SetColor(id, (Color)v) };
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        return new MaterialProperty { Type = typeof(Vector4), Get = () => m.GetVector(id), Set = v => m.SetVector(id, (Vector4)v) };
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                        return new MaterialProperty { Type = typeof(float), Get = () => m.GetFloat(id), Set = v => m.SetFloat(id, (float)v) };
                    default:
                        throw new InvalidOperationException($"{property} is a {shader.GetPropertyType(index)}, which cannot be set this way.");
                }
            }
        }

        // ---- saying what happened ----------------------------------------------

        private static string Path(GameObject go)
        {
            string path = go.name;
            for (Transform t = go.transform.parent; t != null; t = t.parent)
            {
                path = t.name + "/" + path;
            }
            return path;
        }

        private static string Show(object value)
        {
            if (value == null)
            {
                return "null";
            }
            string text = OverrideValues.Serialize(value);
            return text ?? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
