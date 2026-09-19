using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DragNWash.ModFramework.Inspector
{
    // Rigidbodies (3D) and Rigidbody2Ds: read, driven and listed by
    // reflection. The physics modules are not referenced, the same as for
    // colliders, so this library still loads where a module is stripped.
    // Unity 6 renamed velocity to linearVelocity; both names are tried.
    //
    // The physics pause switches Physics.simulationMode (and Physics2D's) to
    // Script, so the engine only moves when Step asks it to; the mode the
    // game had is put back on Resume, when the window closes, and when
    // developer tools are switched off. Experimental.
    internal static class InspectorBodies
    {
        private sealed class Api
        {
            public Type Type;
            public bool Is2D;
            public PropertyInfo Velocity, AngularVelocity, Mass, IsKinematic, BodyType, CenterOfMass;
            public MethodInfo IsSleeping, Sleep, WakeUp;
        }

        private static Api _body3, _body2;
        private static bool _looked;

        // Seconds of travel the velocity arrow shows.
        internal const float ArrowSeconds = 0.25f;

        private static void Look()
        {
            if (_looked)
            {
                return;
            }
            _looked = true;
            _body3 = Load("UnityEngine.Rigidbody, UnityEngine.PhysicsModule", false);
            _body2 = Load("UnityEngine.Rigidbody2D, UnityEngine.Physics2DModule", true);
        }

        private static Api Load(string name, bool is2D)
        {
            Type t = Type.GetType(name);
            if (t == null)
            {
                return null;
            }
            return new Api
            {
                Type = t,
                Is2D = is2D,
                Velocity = t.GetProperty("linearVelocity") ?? t.GetProperty("velocity"),
                AngularVelocity = t.GetProperty("angularVelocity"),
                Mass = t.GetProperty("mass"),
                IsKinematic = t.GetProperty("isKinematic"),
                // A Rigidbody2D says dynamic, kinematic or static; isKinematic is obsolete there.
                BodyType = is2D ? t.GetProperty("bodyType") : null,
                CenterOfMass = t.GetProperty("worldCenterOfMass"),
                IsSleeping = t.GetMethod("IsSleeping", Type.EmptyTypes),
                Sleep = t.GetMethod("Sleep", Type.EmptyTypes),
                WakeUp = t.GetMethod("WakeUp", Type.EmptyTypes),
            };
        }

        private static Api Of(Component c)
        {
            Look();
            if (c == null)
            {
                return null;
            }
            Type t = c.GetType();
            if (_body3 != null && _body3.Type.IsAssignableFrom(t)) return _body3;
            if (_body2 != null && _body2.Type.IsAssignableFrom(t)) return _body2;
            return null;
        }

        internal static bool IsBody(object o) => o is Component c && Of(c) != null;

        // ---- reading -----------------------------------------------------------------

        private static object Get(PropertyInfo p, Component c)
        {
            if (p == null) return null;
            try { return p.GetValue(c, null); }
            catch { return null; }
        }

        private static Vector3 ToVector(object v) => v is Vector3 v3 ? v3 : v is Vector2 v2 ? (Vector3)v2 : Vector3.zero;

        internal static Vector3 Velocity(Component c) => ToVector(Get(Of(c)?.Velocity, c));

        // Degrees a second: a Rigidbody gives radians, a Rigidbody2D degrees.
        internal static float AngularSpeed(Component c)
        {
            object v = Get(Of(c)?.AngularVelocity, c);
            return v is Vector3 w ? w.magnitude * Mathf.Rad2Deg : v is float f ? Mathf.Abs(f) : 0f;
        }

        internal static float Mass(Component c) => Get(Of(c)?.Mass, c) is float m ? m : 0f;

        internal static bool Is2D(Component c) => Of(c)?.Is2D ?? false;

        // Moved by its script rather than by forces (or, in 2D, static).
        internal static bool Kinematic(Component c)
        {
            Api api = Of(c);
            if (api == null) return false;
            if (api.BodyType != null) return Get(api.BodyType, c)?.ToString() != "Dynamic";
            return Get(api.IsKinematic, c) is bool k && k;
        }

        internal static string BodyKind(Component c)
        {
            Api api = Of(c);
            if (api?.BodyType != null) return (Get(api.BodyType, c)?.ToString() ?? "?").ToLowerInvariant();
            return Kinematic(c) ? "kinematic" : "dynamic";
        }

        internal static bool Sleeping(Component c)
        {
            MethodInfo m = Of(c)?.IsSleeping;
            if (m == null) return false;
            try { return m.Invoke(c, null) is bool b && b; }
            catch { return false; }
        }

        internal static Vector3 CenterOfMass(Component c)
        {
            object v = Get(Of(c)?.CenterOfMass, c);
            return v != null ? ToVector(v) : c.transform.position;
        }

        internal static string Describe(Component c)
        {
            string state = Sleeping(c) ? "asleep" : "awake";
            return $"{Velocity(c).magnitude:0.00} m/s, {AngularSpeed(c):0} deg/s, {Mass(c):0.###} kg, {BodyKind(c)}, {state}";
        }

        // ---- the selection's controls -------------------------------------------------

        internal static string Stop(Component c)
        {
            Api api = Of(c);
            if (api == null) return "Not a rigidbody.";
            if (Kinematic(c)) return $"{c.name} is {BodyKind(c)}: it has no velocity of its own to stop.";
            try
            {
                api.Velocity?.SetValue(c, api.Is2D ? (object)Vector2.zero : Vector3.zero, null);
                api.AngularVelocity?.SetValue(c, api.Is2D ? (object)0f : Vector3.zero, null);
                return $"Stopped {c.name}.";
            }
            catch (Exception ex)
            {
                return $"Stop failed: {(ex.InnerException ?? ex).Message}";
            }
        }

        // Kinematic on or off; kept in History so it can be reverted.
        internal static string SetKinematic(Component c, bool on, string where)
        {
            Api api = Of(c);
            PropertyInfo p = api?.BodyType ?? api?.IsKinematic;
            if (p == null || !p.CanWrite) return "This body's kind cannot be changed here.";
            try
            {
                object before = p.GetValue(c, null);
                object after = api.BodyType != null ? Enum.Parse(p.PropertyType, on ? "Kinematic" : "Dynamic") : (object)on;
                p.SetValue(c, after, null);
                InspectorHistory.Record(c, where, p.Name, before, after, () => p.GetValue(c, null), v => p.SetValue(c, v, null));
                return $"{c.name}: {p.Name} = {after}";
            }
            catch (Exception ex)
            {
                return $"{p.Name}: {(ex.InnerException ?? ex).Message}";
            }
        }

        internal static string SleepOrWake(Component c)
        {
            Api api = Of(c);
            bool sleeping = Sleeping(c);
            MethodInfo m = sleeping ? api?.WakeUp : api?.Sleep;
            if (m == null) return "Not available on this body.";
            try
            {
                m.Invoke(c, null);
                return sleeping ? $"Woke {c.name}." : $"{c.name} sleeps until something touches it.";
            }
            catch (Exception ex)
            {
                return $"{m.Name}: {(ex.InnerException ?? ex).Message}";
            }
        }

        // ---- finding them ----------------------------------------------------------------

        private static readonly List<Component> Found = new List<Component>();
        private static float _foundAt = -1f;
        private static GameObject _foundFor;
        private static bool _foundSelectionOnly;

        // Every active Rigidbody and Rigidbody2D, scene-wide or under the
        // selection; kept for half a second, since finding them walks the scene.
        internal static List<Component> In(GameObject selected, bool selectionOnly)
        {
            Look();
            if (Time.realtimeSinceStartup - _foundAt < 0.5f && ReferenceEquals(_foundFor, selected) && _foundSelectionOnly == selectionOnly)
            {
                Found.RemoveAll(c => c == null);
                return Found;
            }
            _foundAt = Time.realtimeSinceStartup;
            _foundFor = selected;
            _foundSelectionOnly = selectionOnly;
            Found.Clear();
            foreach (Api api in new[] { _body3, _body2 })
            {
                if (api == null) continue;
                try
                {
                    if (selectionOnly)
                    {
                        if (selected != null) Found.AddRange(selected.GetComponentsInChildren(api.Type, false));
                    }
                    else
                    {
                        foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(api.Type, FindObjectsSortMode.None))
                        {
                            if (o is Component c && c.gameObject.activeInHierarchy) Found.Add(c);
                        }
                    }
                }
                catch (Exception ex)
                {
                    InspectorPlugin.Log.LogWarning($"[inspector] Finding {api.Type.Name}s failed: {ex.Message}");
                }
            }
            return Found;
        }

        internal static bool Available
        {
            get
            {
                Look();
                return _body3 != null || _body2 != null;
            }
        }

        // ---- drawing (GL lines, pixel matrix, inside GL.Begin(GL.LINES)) -----------------

        // A cross on the centre of mass and an arrow for where the body is
        // heading: ArrowSeconds of travel at its current velocity.
        internal static void DrawMarks(Camera cam, Component c)
        {
            Vector3 com = CenterOfMass(c);
            Vector3 a = cam.WorldToScreenPoint(com);
            if (a.z <= 0) return;
            var pa = new Vector2(a.x, Screen.height - a.y);
            Line(pa + new Vector2(-6, 0), pa + new Vector2(6, 0));
            Line(pa + new Vector2(0, -6), pa + new Vector2(0, 6));
            Vector3 v = Velocity(c);
            if (v.sqrMagnitude < 1e-4f) return;
            Vector3 b = cam.WorldToScreenPoint(com + v * ArrowSeconds);
            if (b.z <= 0) return;
            var pb = new Vector2(b.x, Screen.height - b.y);
            Vector2 d = pb - pa;
            if (d.magnitude < 2f) return;
            Line(pa, pb);
            d.Normalize();
            var n = new Vector2(-d.y, d.x);
            Line(pb, pb - d * 9 + n * 4);
            Line(pb, pb - d * 9 - n * 4);
        }

        private static void Line(Vector2 a, Vector2 b)
        {
            GL.Vertex3(a.x, a.y, 0);
            GL.Vertex3(b.x, b.y, 0);
        }

        // ---- pausing the physics ---------------------------------------------------------

        private sealed class Engine
        {
            public string Name;
            public PropertyInfo Mode;
            public MethodInfo Simulate;
            public object Saved;
        }

        private static List<Engine> _paused;

        internal static bool Paused => _paused != null;

        internal static string Pause()
        {
            if (Paused) return "Physics is already paused.";
            var engines = new List<Engine>();
            foreach (string name in new[] { "UnityEngine.Physics, UnityEngine.PhysicsModule", "UnityEngine.Physics2D, UnityEngine.Physics2DModule" })
            {
                Type t = Type.GetType(name);
                PropertyInfo mode = t?.GetProperty("simulationMode", BindingFlags.Public | BindingFlags.Static);
                if (mode == null || !mode.CanWrite || !mode.PropertyType.IsEnum) continue;
                try
                {
                    var e = new Engine { Name = t.Name, Mode = mode, Simulate = SimulateOf(t), Saved = mode.GetValue(null, null) };
                    mode.SetValue(null, Enum.Parse(mode.PropertyType, "Script"), null);
                    engines.Add(e);
                }
                catch (Exception ex)
                {
                    InspectorPlugin.Log.LogWarning($"[inspector] Pausing {t.Name} failed: {(ex.InnerException ?? ex).Message}");
                }
            }
            if (engines.Count == 0) return "Physics cannot be paused in this game (no simulation mode to switch).";
            _paused = engines;
            InspectorPlugin.Log.LogInfo("[inspector] Physics paused.");
            return "Physics paused: Step moves it one fixed step. Resume, or closing the window, puts it back.";
        }

        internal static string Resume()
        {
            if (!Paused) return "Physics is not paused.";
            foreach (Engine e in _paused)
            {
                try { e.Mode.SetValue(null, e.Saved, null); }
                catch (Exception ex) { InspectorPlugin.Log.LogWarning($"[inspector] Restoring {e.Name} failed: {(ex.InnerException ?? ex).Message}"); }
            }
            _paused = null;
            InspectorPlugin.Log.LogInfo("[inspector] Physics resumed.");
            return "Physics resumed.";
        }

        internal static string Step(int steps = 1)
        {
            if (!Paused) return "Pause the physics first.";
            float dt = Time.fixedDeltaTime;
            steps = Mathf.Clamp(steps, 1, 600);
            foreach (Engine e in _paused)
            {
                if (e.Simulate == null) continue;
                ParameterInfo[] ps = e.Simulate.GetParameters();
                var args = new object[ps.Length];
                args[0] = dt;
                for (int i = 1; i < ps.Length; i++) args[i] = ps[i].DefaultValue;
                try
                {
                    for (int n = 0; n < steps; n++) e.Simulate.Invoke(null, args);
                }
                catch (Exception ex)
                {
                    return $"Step of {e.Name} failed: {(ex.InnerException ?? ex).Message}";
                }
            }
            return $"Stepped {steps} x {dt:0.####} s.";
        }

        // Simulate(float) or, where the version added optional parameters, the
        // overload that takes a float first and defaults for the rest.
        private static MethodInfo SimulateOf(Type t)
        {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "Simulate") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].ParameterType != typeof(float)) continue;
                bool rest = true;
                for (int i = 1; i < ps.Length; i++) rest &= ps[i].HasDefaultValue;
                if (rest) return m;
            }
            return null;
        }

        // ---- console -------------------------------------------------------------------

        internal static string Command(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "pause": return Pause();
                    case "resume": return Resume();
                    case "step": return Step(args.Length > 1 && int.TryParse(args[1], out int n) ? n : 1);
                }
            }
            if (!Available) return "This game has no physics module.";
            var list = new List<Component>(In(null, false));
            list.Sort((a, b) => Velocity(b).sqrMagnitude.CompareTo(Velocity(a).sqrMagnitude));
            var sb = new System.Text.StringBuilder();
            sb.Append($"{list.Count} rigidbody(ies), fastest first{(Paused ? "; physics paused" : "")}:\n");
            for (int i = 0; i < list.Count && i < 30; i++)
            {
                Component c = list[i];
                sb.Append("  ").Append(InspectorModel.PathOf(c.transform)).Append(" (").Append(c.GetType().Name).Append("): ").Append(Describe(c)).Append('\n');
            }
            if (list.Count > 30) sb.Append($"  ... {list.Count - 30} more");
            return sb.ToString().TrimEnd();
        }

        internal static IEnumerable<string> Complete(string[] args)
        {
            if (args.Length == 1)
            {
                foreach (string s in new[] { "pause", "resume", "step" })
                {
                    if (s.StartsWith(args[0], StringComparison.OrdinalIgnoreCase)) yield return s;
                }
            }
        }
    }
}
