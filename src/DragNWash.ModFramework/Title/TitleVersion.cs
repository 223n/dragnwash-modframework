using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace DragNWash.ModFramework.Title
{
    // Like Minecraft Forge's title screen: a line above the game's build id in the
    // corner of the title screen says the framework is running and how many mods
    // loaded, so a player can tell at a glance that mods are in.
    //
    // The game writes its build id in VersionNumber.Start. The label is a copy of
    // that text (same font, size, colour and anchoring) placed just above it.
    internal static class TitleVersion
    {
        private const string Feature = "Version on the title screen";
        private const string LabelName = "ModFrameworkVersion";

        internal static void Install(Harmony harmony)
        {
            if (!GameHooks.Require(ModFramework.Guid, Feature, "VersionNumber", "Start"))
            {
                return;
            }
            try
            {
                MethodInfo start = AccessTools.Method(AccessTools.TypeByName("VersionNumber"), "Start");
                harmony.Patch(start, postfix: new HarmonyMethod(typeof(TitleVersion), nameof(AfterStart)));
            }
            catch (Exception ex)
            {
                GameHooks.Require(ModFramework.Guid, Feature, false, ex.Message);
            }
        }

        private static void AfterStart(MonoBehaviour __instance)
        {
            try
            {
                Add(__instance);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not show the framework version on the title screen: {ex.Message}");
            }
        }

        internal static string Text()
        {
            int mods = Chainloader.PluginInfos.Count;
            return $"Drag'n Wash ModFramework {ModFramework.Version}\n{mods} {(mods == 1 ? "mod" : "mods")} loaded";
        }

        private static void Add(MonoBehaviour versionNumber)
        {
            var original = versionNumber.GetComponent<TMP_Text>();
            Transform parent = versionNumber.transform.parent;
            if (original == null || parent == null || parent.Find(LabelName) != null)
            {
                return;
            }

            GameObject copy = UnityEngine.Object.Instantiate(versionNumber.gameObject, parent, false);
            copy.name = LabelName;
            // The copy must not write the build id over our text when it starts.
            UnityEngine.Object.DestroyImmediate(copy.GetComponent(versionNumber.GetType()));

            var label = copy.GetComponent<TMP_Text>();
            label.text = Text();
            label.enableAutoSizing = false;
            label.fontSize = original.fontSize;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            // Right-aligned to where the build id's text ends, sitting on top of the
            // text itself rather than its (taller, wider) rectangle, so the lines
            // stay on screen in the corner and close to the build id.
            original.ForceMeshUpdate();
            Bounds text = original.textBounds;
            var from = (RectTransform)versionNumber.transform;
            var to = (RectTransform)copy.transform;
            label.alignment = TextAlignmentOptions.BottomRight;
            Vector2 size = label.GetPreferredValues(label.text);
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = new Vector2(1f, 0f);
            to.sizeDelta = new Vector2(size.x + 4f, size.y);
            to.localPosition = new Vector3(from.localPosition.x + text.max.x, from.localPosition.y + text.max.y + 2f, from.localPosition.z);
            to.SetSiblingIndex(from.GetSiblingIndex() + 1);
        }
    }
}
