using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DragNWash.ModFramework.ToolWindow
{
    // On the Steam Deck the trackpad click never reaches IMGUI as a mouse
    // button (Steam Input delivers it as a gamepad stick press; only the
    // pointer motion arrives as a mouse), so nothing in the menu could be
    // pressed. Watch the Input System instead and, when a press arrives that
    // IMGUI did not see, treat the GUI.Button under the pointer as clicked.
    // Hooked into GUI.Button through Harmony so every button in the menu
    // gets it without changes. Pad buttons (A, R2, R3/L3) click, and the
    // sticks / d-pad scroll the lists.
    internal static class VirtualClick
    {
        private static bool _pending;
        private static int _armedFrame;
        private static Func<Rect> _visibleRect;

        public static void Install(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(GUI), "Button",
                new[] { typeof(Rect), typeof(int), typeof(GUIContent), typeof(GUIStyle) });
            if (target == null)
            {
                ToolWindowPlugin.Log.LogInfo("[click] GUI.Button(Rect,int,GUIContent,GUIStyle) not found; pad/trackpad clicks disabled.");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(VirtualClick), nameof(AfterButton)));
            try
            {
                Type clip = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip");
                PropertyInfo vis = clip?.GetProperty("visibleRect", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                MethodInfo getter = vis?.GetGetMethod(true);
                if (getter != null)
                    _visibleRect = (Func<Rect>)Delegate.CreateDelegate(typeof(Func<Rect>), getter);
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogInfo($"[click] GUIClip.visibleRect unavailable: {ex.Message}");
            }
        }

        // Screen-space pointer as IMGUI last reported it (hover still works
        // where clicks do not).
        private static Vector2 _pointer = new Vector2(-1, -1);
        private static bool _held;
        private static Vector2 _pressPointer;

        // Called from Update while the menu is open.
        public static void Poll()
        {
            bool press = false;
            bool held = false;
            // Pad buttons only. A real mouse click already reaches IMGUI as a
            // MouseDown/MouseUp pair; treating it as a pad press too made every
            // menu button fire twice on Windows (a level step moved by two, a
            // flag toggle flipped straight back).
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                // Steam Input reports a trackpad click as a stick press (R3/L3).
                if (pad.buttonSouth.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame
                    || pad.rightStickButton.wasPressedThisFrame || pad.leftStickButton.wasPressedThisFrame)
                    press = true;
                held = pad.buttonSouth.isPressed || pad.rightTrigger.isPressed
                    || pad.rightStickButton.isPressed || pad.leftStickButton.isPressed;
                // Sticks and the d-pad scroll whichever list the pointer is over.
                float dy = pad.rightStick.ReadValue().y + pad.leftStick.ReadValue().y + pad.dpad.ReadValue().y;
                _scrollDelta = Mathf.Abs(dy) > 0.25f ? -dy * ScrollSpeed * Time.unscaledDeltaTime : 0f;
            }
            else
            {
                _scrollDelta = 0f;
            }
            if (press)
            {
                _pending = true;
                _armedFrame = Time.frameCount;
                _pressPointer = _pointer;
                _dragMode = DragMode.None;
                _dragDecided = false;
            }
            // The mouse drags the window through IMGUI itself; adding it here
            // would move the window twice as far.
            _held = held;
            if (!_held) { _dragMode = DragMode.None; _dragDecided = false; }
        }

        private enum DragMode { None, Move, Resize }
        private static DragMode _dragMode;
        private static bool _dragDecided;
        private static Rect _dragStartRect;

        // Called from Update: a held pad button that started on the title bar
        // moves the window, one that started on the corner grip resizes it.
        // Returns true when the rect changed.
        public static bool UpdateDrag(ref Rect window, float titleHeight, float titleRightMargin, float gripSize)
        {
            if (!_held || _pointer.x < 0) return false;
            if (!_dragDecided)
            {
                _dragDecided = true;
                _dragStartRect = window;
                var title = new Rect(window.x, window.y, window.width - titleRightMargin, titleHeight);
                var grip = new Rect(window.xMax - gripSize, window.yMax - gripSize, gripSize, gripSize);
                if (grip.Contains(_pressPointer)) _dragMode = DragMode.Resize;
                else if (title.Contains(_pressPointer)) _dragMode = DragMode.Move;
                else _dragMode = DragMode.None;
            }
            if (_dragMode == DragMode.None) return false;
            Vector2 delta = _pointer - _pressPointer;
            if (delta.sqrMagnitude > 16f) _pending = false;   // a drag is not a click
            if (_dragMode == DragMode.Move)
                window.position = _dragStartRect.position + delta;
            else
                window.size = _dragStartRect.size + delta;
            return true;
        }

        private const float ScrollSpeed = 700f;   // pixels per second at full deflection
        private static float _scrollDelta;
        private static int _scrollFrame = -1;

        // Called before GUI.BeginScrollView. Moves the view under the pointer
        // by this frame's pad scroll; returns true when it moved.
        public static bool ApplyScroll(Rect view, ref Vector2 scroll)
        {
            if (_scrollDelta == 0f) return false;
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return false;
            if (_scrollFrame == Time.frameCount) return false;
            if (!view.Contains(ev.mousePosition)) return false;
            _scrollFrame = Time.frameCount;
            scroll.y = Mathf.Max(0f, scroll.y + _scrollDelta);
            return true;
        }

        // Called from OnGUI for every event. A real MouseDown means IMGUI
        // handles this click itself; drop the pending one so it does not
        // fire twice.
        public static void Observe(Event ev)
        {
            if (ev.type == EventType.Repaint)
            {
                Vector2 p = ev.mousePosition;
                if (p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height) _pointer = p;
            }
            if (ev.type == EventType.MouseDown && _pending)
            {
                _pending = false;
            }
        }

        public static void Cancel()
        {
            _pending = false;
        }

        private static void AfterButton(Rect position, ref bool __result)
        {
            if (!_pending) return;
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return;
            int age = Time.frameCount - _armedFrame;
            if (age > 3) { _pending = false; return; }
            if (age < 1) return;   // give IMGUI's own MouseDown a chance first
            if (!position.Contains(ev.mousePosition)) return;
            if (_visibleRect != null)
            {
                try { if (!_visibleRect().Contains(ev.mousePosition)) return; } catch { }
            }
            _pending = false;
            __result = true;
        }
    }
}
