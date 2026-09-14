# Design memo

[日本語](DESIGN.ja.md)

Status: draft, September 2026. Nothing here is final; open an issue to discuss any part of it.

## Why a framework

[Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) had to build a lot of machinery to hook into the game: a setting in the game's Options screen, an in-game menu that works with a gamepad and on the Steam Deck, a hook on every text component, the Yarn line that is about to be shown, fonts that do not crash Direct3D 12, save snapshots. Most of that is not specific to translation. Any other mod would have to build it again, and every one of them would break separately when the game updates.

The game update of September 14, 2026 (build `9/12/2026_a93aa21a`) showed how that goes: typo fixes in 25 lines, two new options, a new pause button and a new save format, all in one patch.

The framework puts the code that touches the game in one place and gives mods a stable API instead.

## Goals

- **A Mods screen inside the game.** Players see and configure every installed mod from the game's own menus, like Minecraft Forge's mod list. See [The Mods screen](#the-mods-screen).
- **One place absorbs game updates.** Mods use the framework's types, never the game's classes directly for the features the framework covers. When the game changes, the framework follows and the mods keep working.
- **Fail soft.** If a patch target disappears after an update, that feature reports itself unavailable and logs why. The game and the other features keep running.
- **Safe on every platform the game runs on.** Windows (Direct3D 12 and 11), Windows on ARM, Steam Deck / Linux. Knowledge such as the Direct3D 12 upload crash (UUM-140564) lives in the framework, not in each mod.
- **No game files.** Like the localization mod, the repository and releases never contain the game's assets, script or binaries.

## Non-goals (for now)

- Loading new 3D content (custom dragons, models). Possible later, but it depends on how the developers feel about it.
- Replacing BepInEx or Harmony. The framework is a BepInEx 5 plugin and uses Harmony internally.
- macOS, until BepInEx can load on Unity 6.3 there (NeighTools/UnityDoorstop#108).

## Packaging and versioning

- BepInEx 5 plugin, GUID `com.tomxv.dragnwash.modframework`, assembly `DragNWash.ModFramework.dll`, namespace `DragNWash.ModFramework`.
- A mod depends on it with `[BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]`, optionally with a minimum version.
- Semantic versioning. **0.x** while the API takes shape: minor versions may break the API, and the changelog says how. **1.0** once the localization mod runs on it and the API has settled; after that, breaking changes only in a new major version.
- Public API: everything `public` in the `DragNWash.ModFramework` namespace. Anything touching game types stays `internal`.

## The Mods screen

The centre of the framework for players: a screen listing every installed mod, in the spirit of Minecraft Forge's mod list, reached from the game's own Options screen so it feels like part of the game.

### What players see

- A **Mods** button in the Options screen, in the column of buttons with Back, Reset to defaults and Save. Options is reachable from both the title screen and the pause menu, so the Mods screen is too, and the title screen and pause menu stay exactly as the game made them.
- The Mods screen: the list of mods on the left, details of the selected mod on the right.
  - Icon, name, version, authors, description, website, and which framework version it needs.
  - **Settings** for that mod, shown as rows in the same style as the game's Options screen.
  - A notice when a mod failed to load or a feature is unavailable on this game build.
  - An **on/off switch** for each mod, applied from the next launch (see below).
- **Back** (button, Esc or the pad's cancel) returns to the Options screen.
- Works with mouse, gamepad and on the Steam Deck, like the rest of the game's menus.

Every BepInEx plugin appears in the list, even one that knows nothing about the framework (GUID, name and version come from BepInEx). A mod built on the framework can add the rest: description, authors, website, icon and settings.

### How it fits into the game

The game's menus are simple enough to extend without touching its files (checked against build `9/12/2026_a93aa21a`):

- Each screen is a `Menu` component registered with `MenuManager` by its GameObject name: `Menu_Main` (the title screen, or the pause menu inside a level), `Menu_Options`, `Menu_SlotsLoad` and so on.
- `Menu_Options` is laid out as `Container/Panel` with `TitleImage`, a `Scroll View` whose rows the settings library generates, and `LeftButtons` holding `Back`, `ResetToDefaults` and `Save`. The same layout is used on the title screen and in levels.
- A button raises an intent named after its GameObject (`MenuWithButtons`), and the current menu answers with a transition to another menu by name, such as `new MenuResponseTransition("Menu_Options", ...)`.
- So the framework can:
  1. clone a button in `Menu_Options/Container/Panel/LeftButtons`, rename it `Mods` and give it its own label,
  2. patch `MenuOptions.OnEvent` so the `Mods` intent transitions to `Menu_Mods`,
  3. create `Menu_Mods` as a `Menu` subclass built from the Options screen's layout, register it with `MenuManager`, and send `Back` to `Menu_Options`.
- To check in the game: that pending changes on the Options screen are kept when the player goes to the Mods screen and comes back, rather than being reverted as Back does.
- The game's buttons have their words painted into the artwork. The Mods button and everything on the Mods screen are TextMeshPro text in the game's own TMP font instead, so no artwork is drawn or copied and every label can be translated (the localization mod's text hook picks them up like any other UI text).
- Mod icons, if a mod provides one, are loaded at startup, which is safe on Direct3D 12.

### Turning mods on and off

A mod cannot be unloaded from a running game, so the switch takes effect from the next launch, and the screen says so.

- The framework ships a small BepInEx preloader patcher (`BepInEx/patchers/DragNWash.ModFramework.Preloader.dll`). Patchers run before any plugin assembly is loaded, so the files are not in use yet.
- On launch the patcher reads the framework's list of disabled mods and renames their DLLs to `.dll.disabled` (and back when a mod is switched on again). BepInEx then simply does not see a disabled mod, and a player can undo it by hand by renaming the file.
- The framework itself cannot be switched off from its own screen.
- Switching off a mod that other mods depend on (`BepInDependency`) lists those mods and asks before doing it.

### For mod authors

```csharp
ModFramework.Register(new ModInfo
{
    Guid = MyPlugin.Guid,
    Description = "Adds ...",
    Authors = new[] { "Me" },
    Website = "https://github.com/me/mymod",
});
```

Settings shown on the Mods screen come from the mod's BepInEx config entries (`ConfigEntry<bool>` becomes a toggle, a ranged number a slider, an enum a dropdown), so a mod gets a settings page without writing UI. The Settings API can add rows to the game's own Options screen as well.

## API candidates

Most areas come from working code in the localization mod (file names refer to `src/DragNWashLocalization/` there).

| Area | What mods get | Comes from |
|---|---|---|
| Mods screen | A Mods button in the Options screen, the mod list and details, on/off switches applied at the next launch, settings pages generated from BepInEx config, `ModFramework.Register(ModInfo)` | new; uses the menu knowledge from `OptionsLanguage.cs` |
| Game info | Unity version, graphics API, platform, game build, "is this build known to work" | `Plugin.cs` startup checks |
| Settings | Add a row to the game's Options screen (dropdown, toggle, slider) that previews on change, saves with the game's Save button and reverts with Back | `OptionsLanguage.cs` |
| Tool window | A shared developer window (F1 by default) for debug tools, where each mod registers a tab; cursor unlock, input blocking behind the window, gamepad and Steam Deck trackpad clicks, a CJK-capable menu font | `Plugin.ImGui.cs`, `CursorUnlock.cs`, `InputBlocker.cs`, `VirtualClick.cs`, `MenuFontBundle.cs` |
| Text | An event before a TMP text is shown, with the source text and the component, where a mod can replace it; re-apply on demand (e.g. after a language switch) | `TmpTextPatches.cs` |
| Dialogue | Events for a line about to be shown and options about to be offered, with line ID, speaker and node; the loaded Yarn project | `LineIdContext.cs`, `SpeakerLookup.cs`, `DialogueDumper.cs` |
| Flags and saves | Read game flags; snapshots of save slots before a mod changes anything | `FlagCatalog.cs`, `SaveHistory.cs` |
| Assets | Load fonts, textures and asset bundles at a safe moment (at startup on Direct3D 12) | `FontFallback.cs`, `MenuFontBundle.cs` |
| Installer | One installer that puts BepInEx, the framework and chosen mods in place (Windows, Steam Deck) | `installer/` |

## Order of work

1. **0.1 Skeleton.** Plugin, `ModFramework`, `GameInfo`, build and repository rules. (this version)
2. **0.2 Mods screen.** The Mods button in the Options screen, the list and details of installed mods, `ModInfo`, and switching mods on and off with the preloader patcher.
3. **0.3 Settings.** Settings pages on the Mods screen generated from BepInEx config, and the API for rows in the game's Options screen, with the localization mod's language picker as the first user.
4. **0.4 Tool window.** The shared F1 window for developer tools, with cursor and input handling.
5. **0.5 Text.** The text event.
6. **0.6 Dialogue.** Line and option events.
7. **Assets, flags and saves, installer**, in whichever order the localization mod needs them.
8. **1.0** when Drag'n Wash Localization v1.0.0 runs on the framework.

Each step moves one feature out of the localization mod, and the localization mod switches to the framework for that feature before the next step starts. Every step is tested in the game on Windows and on the Steam Deck.

## Following game updates

- Keep a table of game builds the framework was checked against.
- For each patch target, check it exists at startup and log a clear line when it does not.
- After an update: decompile and diff the game assembly, diff the Yarn string table, update the table, release.

## Open questions

- How mods show up in the tool window when several register tabs (order, naming).
- Whether the installer lives here or stays in the localization repository.
- Distribution beyond GitHub Releases.
- The developers' view on mods, which matters more for a framework than for a translation.
