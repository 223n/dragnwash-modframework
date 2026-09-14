using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DragNWash.ModFramework.ToolWindow
{
    // When a gamepad is present (always on the Steam Deck) the game keeps the
    // cursor locked, so IMGUI never sees a pointer and the menu cannot be
    // clicked. The game has its own ticket-based unlock used by its menus;
    // borrow it while ours is open, and fall back to Cursor.lockState when
    // the API is not there.
    internal static class CursorUnlock
    {
        private static object _ticket;
        private static bool _held;
        private static bool _fallback;
        private static CursorLockMode _savedLock;
        private static bool _savedVisible;

        // Set by the plugin while the window is open; read by the Harmony postfixes.
        public static bool MenuOpen;

        // The game re-locks the cursor from its own update whenever a gamepad
        // is active, which undoes any one-off unlock. Patch the places that do
        // it so the unlock holds while the menu is up.
        public static void Install(Harmony harmony)
        {
            Type t = AccessTools.TypeByName("GameStateManager");
            if (t == null)
            {
                ToolWindowPlugin.Log.LogInfo("[cursor] GameStateManager not found; relying on Cursor.lockState only.");
                return;
            }
            int patched = 0;
            foreach (string name in new[] { "CheckMouseLock", "UpdateMouseCursorUnlock", "LateUpdate" })
            {
                MethodInfo m = AccessTools.Method(t, name);
                if (m == null) continue;
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(CursorUnlock), nameof(AfterGameLock)));
                patched++;
            }
            ToolWindowPlugin.Log.LogInfo($"[cursor] Patched {patched} GameStateManager method(s) to keep the cursor free while the menu is open.");
        }

        private static void AfterGameLock()
        {
            if (!MenuOpen) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        public static void Hold(MonoBehaviour owner)
        {
            if (_held) return;
            _held = true;
            MenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            try
            {
                Type t = AccessTools.TypeByName("GameStateManager");
                UnityEngine.Object mgr = t != null ? UnityEngine.Object.FindAnyObjectByType(t) : null;
                MethodInfo req = t != null ? AccessTools.Method(t, "RequestCursorUnlock") : null;
                if (mgr != null && req != null)
                {
                    _ticket = req.Invoke(mgr, new object[] { owner, true });
                    ToolWindowPlugin.Log.LogInfo("[cursor] Unlocked through the game's GameStateManager (player input paused).");
                    return;
                }
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogInfo($"[cursor] Game unlock failed, using Cursor.lockState instead: {ex.InnerException?.Message ?? ex.Message}");
            }
            _fallback = true;
            _savedLock = Cursor.lockState;
            _savedVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Some games re-lock every frame; keep the fallback in force.
        public static void Tick()
        {
            if (_held && Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public static void Release()
        {
            if (!_held) return;
            _held = false;
            MenuOpen = false;
            try
            {
                if (_ticket != null)
                {
                    Type t = AccessTools.TypeByName("GameStateManager");
                    UnityEngine.Object mgr = t != null ? UnityEngine.Object.FindAnyObjectByType(t) : null;
                    MethodInfo rel = t != null ? AccessTools.Method(t, "ReleaseCursorUnlock") : null;
                    if (mgr != null && rel != null)
                    {
                        var args = new object[] { _ticket };
                        rel.Invoke(mgr, args);
                    }
                }
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogInfo($"[cursor] Release failed: {ex.InnerException?.Message ?? ex.Message}");
            }
            _ticket = null;
            if (_fallback)
            {
                Cursor.lockState = _savedLock;
                Cursor.visible = _savedVisible;
                _fallback = false;
            }
        }
    }
}
