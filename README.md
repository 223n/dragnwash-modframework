# Drag'n Wash ModFramework

[日本語](README.ja.md)

A prerequisite mod for [Drag'n Wash](https://store.steampowered.com/app/4739660/) (BepInEx 5). It keeps the code that hooks into the game in one place and gives other mods a stable API for it: a Mods screen reached from the game's Options screen (like Minecraft Forge's mod list, with on/off switches), settings in the game's Options screen, text and dialogue events, safe asset loading on Direct3D 12, and more. When the game updates, only the framework has to follow.

> [!WARNING]
> **Early development.** Version 0.1 is only the skeleton; there is nothing for players to install yet. The API will change until 1.0.

The first mod built on it will be [Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization), as its v1.0.0.

See [docs/DESIGN.md](docs/DESIGN.md) for goals, the planned API and the order of work.

## For mod developers

Reference `DragNWash.ModFramework.dll` and declare the dependency so BepInEx loads the framework first:

```csharp
[BepInPlugin("com.example.mymod", "MyMod", "1.0.0")]
[BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
public class MyMod : BaseUnityPlugin
{
    private void Awake()
    {
        ModFramework.Ready += () =>
        {
            if (GameInfo.IsDirect3D12)
            {
                // Load fonts and textures now, not later.
            }
        };
    }
}
```

## Building

1. Install the .NET SDK and have BepInEx 5.4.23.5 installed in the game.
2. Copy the reference assemblies from your own game install (they are never committed):

   ```bash
   pwsh tools/copy-libs.ps1
   ```

   Pass `-GamePath` if the game is not in the default Steam library.
3. Build:

   ```bash
   dotnet build src/DragNWash.ModFramework/DragNWash.ModFramework.csproj -c Release
   ```

The DLL goes to `src/DragNWash.ModFramework/bin/Release/`. To try it, copy it to `<Game>/BepInEx/plugins/DragNWash.ModFramework/`.

## Rules for this repository

- Never commit the game's files, BepInEx binaries or anything from `libs/`. A check on every push and pull request enforces it.
- Code that touches game classes stays `internal`; mods only see the framework's own types.

## A note to the developers

This is an unofficial fan project and is not affiliated with Gator Dragon Games. It contains no game assets or code and does not modify the game's files (BepInEx loads it at runtime). If the development team has any concerns, please open an issue or contact the maintainer, and it will be changed or taken down.

## License

[MIT](LICENSE)
