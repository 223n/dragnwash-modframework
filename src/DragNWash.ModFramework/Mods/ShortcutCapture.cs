using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DragNWash.ModFramework.Mods
{
    // Takes the next key pressed as a KeyboardShortcut for the selected setting,
    // with whichever modifier keys are held. Lives on the setting's Change
    // button while capturing and removes itself when done; Esc cancels. Keys are read through BepInEx's
    // UnityInput, which works whether the game uses the old Input class or the
    // Input System (this game has the old one switched off).
    internal sealed class ShortcutCapture : MonoBehaviour, IDeselectHandler
    {
        internal ModsMenu Menu;
        internal ConfigItem Item;
        internal TMP_Text Label;

        private static readonly KeyCode[] Modifiers =
        {
            KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftCommand, KeyCode.RightCommand,
        };

        // Every keyboard key code once; not the mouse buttons or joystick buttons.
        private static readonly KeyCode[] Keys = Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>().Distinct()
            .Where(k => k != KeyCode.None && !(k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) && k < KeyCode.JoystickButton0)
            .Where(k => Array.IndexOf(Modifiers, k) < 0)
            .ToArray();

        private string _before;
        private int _startedFrame;

        private void Start()
        {
            _startedFrame = Time.frameCount;
            if (Label != null)
            {
                _before = Label.text;
                Label.text = ModsMenu.TextPressKey;
            }
        }

        private void Update()
        {
            // Not the frame of the click that started this: its own key (Enter
            // or Space on a focused button) would be taken as the shortcut.
            if (Time.frameCount == _startedFrame)
            {
                return;
            }
            try
            {
                foreach (KeyCode key in Keys)
                {
                    if (!UnityInput.Current.GetKeyDown(key))
                    {
                        continue;
                    }
                    // Esc (the game's Back) stops taking a key instead of becoming one.
                    if (key == KeyCode.Escape)
                    {
                        Cancel();
                        return;
                    }
                    KeyCode[] held = Modifiers.Where(m => UnityInput.Current.GetKey(m)).ToArray();
                    string before = Item?.SerializedText;
                    Item?.SetShortcut(new KeyboardShortcut(key, held));
                    Finish(before);
                    return;
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not read the keyboard for a shortcut: {ex.Message}");
                Cancel();
            }
        }

        // The pad or the pointer moved on: the next key pressed is not for
        // this setting (it may be typed into the search field).
        public void OnDeselect(BaseEventData eventData)
        {
            Cancel();
        }

        internal void Cancel()
        {
            if (Label != null && _before != null)
            {
                Label.text = _before;
            }
            Destroy(this);
        }

        private void Finish(string before)
        {
            Destroy(this);
            Menu?.AfterCapture(Item, before);
        }
    }
}
