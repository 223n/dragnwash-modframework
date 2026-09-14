using BepInEx;

namespace DragNWash.ModFramework
{
    // The BepInEx entry point. It only starts the framework; everything other
    // mods use lives in the public static classes (ModFramework, GameInfo, ...)
    // so they never need a reference to this component.
    [BepInPlugin(ModFramework.Guid, ModFramework.Name, ModFramework.Version)]
    internal sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            ModFramework.Initialize(Logger);
        }
    }
}
