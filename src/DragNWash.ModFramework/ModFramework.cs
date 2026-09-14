using System;
using BepInEx.Logging;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Identity and lifetime of Drag'n Wash ModFramework.
    /// </summary>
    /// <remarks>
    /// A mod that uses the framework declares the dependency so BepInEx loads
    /// the framework first:
    /// <code>
    /// [BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
    /// </code>
    /// </remarks>
    public static class ModFramework
    {
        /// <summary>BepInEx GUID to depend on.</summary>
        public const string Guid = "com.tomxv.dragnwash.modframework";

        /// <summary>Plugin name shown by BepInEx.</summary>
        public const string Name = "DragNWash.ModFramework";

        /// <summary>
        /// Framework version. Semantic versioning: while it is 0.x the public API
        /// may still change between minor versions; from 1.0 on, breaking changes
        /// only come with a new major version. Keep in sync with the csproj.
        /// </summary>
        public const string Version = "0.1.0";

        private static ManualLogSource _log;

        /// <summary>True once the framework has finished starting.</summary>
        public static bool IsReady { get; private set; }

        /// <summary>
        /// Raised once when the framework has finished starting. A handler added
        /// after that point is called immediately.
        /// </summary>
        public static event Action Ready
        {
            add
            {
                if (IsReady)
                {
                    value?.Invoke();
                }
                else
                {
                    _ready += value;
                }
            }
            remove { _ready -= value; }
        }

        private static Action _ready;

        internal static ManualLogSource Log => _log;

        internal static void Initialize(ManualLogSource log)
        {
            if (IsReady)
            {
                return;
            }

            _log = log;
            _log.LogInfo($"{Name} {Version} on Unity {GameInfo.UnityVersion} / {GameInfo.GraphicsApi}");

            IsReady = true;
            Action handlers = _ready;
            _ready = null;
            if (handlers == null)
            {
                return;
            }

            // One failing subscriber must not stop the others or the framework.
            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    _log.LogError($"A Ready handler threw: {ex}");
                }
            }
        }
    }
}
