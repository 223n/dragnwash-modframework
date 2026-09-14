using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DragNWash.ModFramework.ToolWindow
{
    // IMGUI draws over the game but does not stop the game reading input, so
    // clicking a button in the debug window also clicks whatever sits behind it
    // and dragging the window turns the camera. Nothing in OnGUI can prevent
    // that: uGUI reads through the EventSystem and the game through the Input
    // System, neither of which consults IMGUI.
    //
    // The first attempt deactivated PlayerInput components. This game has none
    // - its own assembly never references the type and drives InputActionAsset
    // directly - so that suspended nothing while looking like it worked.
    // Disable the enabled action maps instead, and remember exactly which ones
    // were enabled so the restore puts back that set rather than everything.
    //
    // The second attempt also disabled the EventSystem, which left the game
    // permanently unclickable: EventSystem.current is backed by a registry that
    // the component removes itself from in OnDisable, so the moment it was
    // disabled the property went null and the restore had nothing to turn back
    // on. Nothing here touches the EventSystem now - the UI module drives uGUI
    // through input actions, so suspending the maps already stops it.
    internal static class InputBlocker
    {
        private static readonly List<InputActionMap> SuspendedMaps = new List<InputActionMap>();

        private static bool _blocking;
        private static bool _reported;

        public static bool IsBlocking => _blocking;

        public static void SetBlocking(bool blocking)
        {
            if (blocking == _blocking)
            {
                return;
            }

            if (blocking)
            {
                Block();
            }
            else
            {
                Unblock();
            }
        }

        private static void Block()
        {
            _blocking = true;
            SuspendActions();

            // Once, so a block that suspends nothing is visible in the log
            // rather than passing for a working one.
            if (!_reported)
            {
                _reported = true;
                ToolWindowPlugin.Log.LogInfo($"[input] Blocking while the pointer is over the menu. Action maps suspended={SuspendedMaps.Count}");
                if (SuspendedMaps.Count == 0)
                {
                    ToolWindowPlugin.Log.LogInfo("[input] No enabled action maps were found, so gameplay input is NOT blocked.");
                }
            }
        }

        private static void Unblock()
        {
            _blocking = false;
            RestoreActions();
        }

        private static void SuspendActions()
        {
            SuspendedMaps.Clear();
            try
            {
                // Action assets are ScriptableObjects, so every loaded one shows
                // up here whether or not anything holds a reference we can see.
                foreach (InputActionAsset asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
                {
                    if (asset == null)
                    {
                        continue;
                    }

                    // Snapshot: Disable() mutates the asset's enabled state.
                    var maps = new List<InputActionMap>();
                    foreach (InputActionMap map in asset.actionMaps)
                    {
                        maps.Add(map);
                    }

                    foreach (InputActionMap map in maps)
                    {
                        if (map == null || !map.enabled)
                        {
                            continue;
                        }
                        map.Disable();
                        SuspendedMaps.Add(map);
                    }
                }
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogInfo($"[input] Could not suspend action maps: {ex.Message}");
            }
        }

        private static void RestoreActions()
        {
            try
            {
                foreach (InputActionMap map in SuspendedMaps)
                {
                    if (map != null)
                    {
                        map.Enable();
                    }
                }
            }
            catch (Exception ex)
            {
                ToolWindowPlugin.Log.LogInfo($"[input] Could not restore action maps: {ex.Message}");
            }
            finally
            {
                SuspendedMaps.Clear();
            }
        }
    }
}
