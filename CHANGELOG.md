# Changelog

Versions of the core and of each library are separate, and follow semantic versioning: from 1.0.0 on, a change that breaks the public API comes only with a new major version.

## Unreleased

### Core 1.2.0

- Experimental. `DeveloperTools`: one switch for everything meant for mod makers and translators, `[Developer] Tools`, **off by default** (Options → Mods → Drag'n Wash ModFramework → Developer tools). Off, the Tool window does not open (F1 or `ToolWindow.Open`) and texture reloading is refused. Mods keep their own developer features behind `DeveloperTools.Enabled`, `Changed` and `WhenEnabled`; see GUIDE rule 8.
- Experimental. The Mods screen's settings pages edit text values: strings, keyboard shortcuts, colours and anything else BepInEx writes to the config file as text get a text field (Enter or leaving the field applies it; a value the config parser refuses is put back with the reason). A keyboard shortcut also has **Capture key**, which takes the next key pressed with the modifiers held. Before, these values could only be changed in the config file.
- Experimental. `GameEvents`: the game's events received once and handed to each mod on its own. `OnSceneLoaded`, `OnSceneUnloaded`, `OnGameStarted` (the title screen's first appearance; late handlers run at once), `OnQuitting`, each registered with the mod's GUID, and `Remove(guid)`. A handler that throws is logged and shown on the Mods screen under its mod, the other mods' handlers still run, and a handler that fails three times in a row is switched off for the session; a handler slower than 100 ms is noted in the debug log. The Assets library's texture replacements use it. GUIDE rule 9.
- Experimental. `SettingMeta` and `SectionMeta`, put in a `ConfigDescription`'s tags: a display name, an order, **Advanced** (hidden until "Show advanced settings" at the top of the page is turned on) and **RequiresRestart** (the page says so under the description); a section's display name, description and order. Tags with the same member names, including ConfigurationManager's `IsAdvanced`, `DispName` and `Order`, are read the same way, so a mod need not reference the framework.
- `GameHooks.Unavailable(ownerGuid, feature, reason)` marks a feature unavailable for a reason other than a missing game member (switched off after a crash, refused on this renderer), shown on the Mods screen like a failed check.

### Tool window 1.1.0

- Experimental. A **Console** tab: every BepInEx log line with its level and source, in colour, the last 2,000 kept; which levels and sources are shown is the player's choice, saved in the config and changeable from the tab, from commands (`log show`, `log level`, `log filter`) and from the Mods screen. Mods register commands with `ToolWindow.AddCommand`; built in: `help`, `log`, `mods`, `scene`. `ToolWindow.ErrorColor` and `WarningColor`. See [docs/CONSOLE.md](docs/CONSOLE.md).

### Assets 1.1.0

- Experimental. Texture replacements: a PNG at `BepInEx/plugins/<Mod>/assets/textures/<texture name>.png` takes the place of the game texture of that name in every material and sprite, read at startup and applied at each scene load; two mods replacing the same texture are both named, never overridden silently. `AssetCatalog` lists loaded textures, materials, meshes and shaders, and the Tool window gets an **Assets** tab. See [docs/ASSET_TOOL.md](docs/ASSET_TOOL.md).
- Experimental. **Reload files** in the Assets tab re-reads changed replacement PNGs while the game runs, and `[Reload] WatchFiles` does it by itself when a file changes (never on Direct3D 12). A marker file catches a crash during a reload at the next start: the Mods screen says so and reloading is switched off until turned back on. Unreadable files keep their previous texture and are listed with the reason.
- Experimental. The `assets` console command: `assets textures [filter]`, `assets replacements`, `assets apply`, `assets reload`.
### Dialogue 1.1.0

- Experimental. `LineKey` and `LineResolver`: keys for a line of dialogue that carry no text and survive a game update editing the line (line ID, exact hash, normalized hash, fingerprint), tried strongest first; matches by anything but the exact text are flagged for review. Same definitions in `tools/linekeys.py`, checked against `ci/linekey-vectors.json` in CI. See [docs/STABLE_LINE_KEYS.md](docs/STABLE_LINE_KEYS.md).

## 2026-09-15: hand-made icon

Released together with Drag'n Wash Localization v1.1.2. The libraries stay at 1.0.0.

### Core 1.1.2

- The Mods screen icon is the "Dg" monogram from the new hand-made logo. The READMEs open with the hand-made logo too.
- The preloader patcher is unchanged; its version follows the core.

## 2026-09-15: icon

Released together with Drag'n Wash Localization v1.1.1. The libraries stay at 1.0.0.

### Core 1.1.1

- The framework has its own icon on the Mods screen (`icon.png` next to the DLL), and the READMEs open with the logo.
- The preloader patcher is unchanged; its version follows the core.

## 2026-09-15: update notices

Released together with Drag'n Wash Localization v1.1.0. The libraries stay at 1.0.0.

### Core 1.1.0

- Shared installer for every mod: `Install.exe` (Windows, no PowerShell) and `install-steamdeck.sh` (Steam Deck / Linux) read the mod's `mod-install.json`. They install BepInEx (pinned SHA-256), never replace a newer framework with an older one, keep the player's files on update, write the mod's choices to its config, and on uninstall keep the framework and BepInEx while other mods need them. Shipped in the release zip under `installer/`; see [docs/INSTALLER.md](docs/INSTALLER.md).
- Uninstall from the Mods screen: press **Uninstall** twice, and the preloader patcher removes the mod's folder at the next launch, keeping the player's data listed in `mod-install.json`. The framework, its libraries and BepInEx are not removable from there.

- Update notices. A mod that names its GitHub repository (`ModInfo.UpdateRepository = "owner/name"`) is checked against the repository's latest release once a day. The Mods screen tags the mod with **Update**, shows the new version and opens its release page, and the title screen says how many updates are available. Nothing is downloaded or changed. Drafts and pre-releases are never offered. Players can switch it off in the framework's settings on the Mods screen (`[Updates] Check for updates`). The framework checks itself the same way.

## 2026-09-15: first release

First release, together with [Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) v1.0.0, the first mod built on the framework.

### Core 1.0.0

- Mods screen, reached from the game's Options screen: every BepInEx plugin with its name, version, description, authors, website and icon (`ModFramework.Register(ModInfo)`, or read from the DLL and a Thunderstore manifest), plugins that did not load and why, and preloader patchers.
- On/off switches, applied by the preloader patcher at the next launch; switching off a mod other mods need asks first.
- Settings pages generated from BepInEx config, and rows in the game's own Options screen (`GameOptions`).
- Extension points: service registry (`Services`), health checks for patched game methods (`GameHooks`), extra Mods screen pages (`ModFramework.AddModsPage`), libraries (`ModInfo.IsLibrary`) with the mods that need them and the mods each one uses.
- Detection of game methods patched by more than one mod, shown as a Conflict.
- The title screen shows "Drag'n Wash ModFramework <version>" and how many mods loaded, just above the game's build id, like Minecraft Forge.
- Works with mouse, gamepad and on the Steam Deck: a thin white frame shows the selected item, and A, R2 and the trackpad click press it. The layout follows window resizes and full screen.

### Text 1.0.0

- `GameText.AddRewriter`, `RefreshAll`, `TryGetSource`: see and replace every TextMeshPro text before the game shows it, in an explicit order, and apply the rewriters again after something they depend on changed.

### Dialogue 1.0.0

- `GameDialogue.LineShowing`, `OptionShowing`, `NodeStarted`, `CurrentNode`, `TryGetLine`: the line of dialogue or option about to be shown, with line ID, speaker and node.

### Tool window 1.0.0

- One shared window (F1 by default, `[General] ToggleKey`) where mods add tabs with `ToolWindow.AddTab`. Frees the cursor, blocks game input under the window, turns gamepad and Steam Deck trackpad presses into clicks, and draws with a font that has Japanese and Chinese glyphs. A tab that throws is turned off with its error shown; the other tabs keep working.

### Assets 1.0.0

- `GameFonts`: one fallback font chain for the whole game, prepared per language at startup so Direct3D 12 does not crash. `GameAssets.LoadTexture` and `LoadBundle`, cached per file.

### Flags and saves 1.0.0

- `GameSaves`: save slots, level and flags, edits that snapshot first, restore, and a history of every version in `BepInEx/SaveHistory`. `GameFlags`: the flag catalog from CSV files.
