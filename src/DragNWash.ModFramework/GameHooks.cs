using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Checks that the game code a mod or library patches still exists on the
    /// running game build, and remembers what is missing so the Mods screen can
    /// say so. Check before patching, and skip the feature when a check fails.
    /// </summary>
    public static class GameHooks
    {
        private static readonly Dictionary<string, List<string>> Missing = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>
        /// True when <paramref name="typeName"/> exists in the game and, if given, has a
        /// method called <paramref name="methodName"/>. A failure is logged and shown on
        /// the Mods screen under <paramref name="ownerGuid"/> with <paramref name="feature"/>.
        /// </summary>
        /// <param name="ownerGuid">BepInEx GUID of the mod that needs it.</param>
        /// <param name="feature">What stops working, for players, e.g. "Dialogue events".</param>
        /// <param name="typeName">Full type name, e.g. "Yarn.Unity.LinePresenter".</param>
        /// <param name="methodName">Method name, or null to check the type only.</param>
        public static bool Require(string ownerGuid, string feature, string typeName, string methodName = null)
        {
            Type type = AccessTools.TypeByName(typeName);
            bool ok = type != null && (methodName == null || AccessTools.Method(type, methodName) != null);
            if (!ok)
            {
                Report(ownerGuid, feature, methodName == null ? typeName : typeName + "." + methodName);
            }
            return ok;
        }

        /// <summary>
        /// Records a check the mod made itself. Returns <paramref name="passed"/>.
        /// </summary>
        public static bool Require(string ownerGuid, string feature, bool passed, string detail)
        {
            if (!passed)
            {
                Report(ownerGuid, feature, detail);
            }
            return passed;
        }

        /// <summary>Features of <paramref name="ownerGuid"/> that failed a check on this game build.</summary>
        public static IReadOnlyList<string> UnavailableFeatures(string ownerGuid)
        {
            lock (Missing)
            {
                return ownerGuid != null && Missing.TryGetValue(ownerGuid, out List<string> list) ? list.ToArray() : new string[0];
            }
        }

        private static void Report(string ownerGuid, string feature, string detail)
        {
            string key = ownerGuid ?? "";
            lock (Missing)
            {
                if (!Missing.TryGetValue(key, out List<string> list))
                {
                    Missing[key] = list = new List<string>();
                }
                if (!list.Contains(feature))
                {
                    list.Add(feature);
                }
            }
            ModFramework.Log.LogWarning($"{ownerGuid ?? "A mod"}: \"{feature}\" is unavailable on this game build: {detail} was not found.");
        }
    }
}
