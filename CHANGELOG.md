# Changelog

Versions of the core and of each library are separate. Nothing has been released yet.

## Unreleased

### Core 0.4.0

The version was still 0.2.0 while the settings (0.3) and extension points (0.4) steps were done; it now matches the order of work in docs/DESIGN.md.

- Mods screen: mod icons (`ModInfo.Icon`, `ModInfo.IconPath`) and a "Uses" line listing the mods each one depends on, with the minimum version when one is declared.
- Mods screen: the list and the details keep their shares of the screen when the window is resized or switched to full screen, and the details are laid out again at the new size. Row labels are placed by their measured width, so a library tag no longer overlaps "On" in a narrow window.
- Mods screen: detection of game methods patched by more than one mod, shown as a Conflict.
- Extension points: service registry (`Services`), health checks (`GameHooks`), extra Mods screen pages (`ModFramework.AddModsPage`), libraries (`ModInfo.IsLibrary`).
- Settings pages generated from BepInEx config; rows in the game's Options screen (`GameOptions`).
- On/off switches applied by the preloader patcher at the next launch.

### Text 0.1.1

- A text the game cleared, or changed through a path the library does not hook, is no longer put back by `GameText.RefreshAll`. Before, a language switch on the Options screen could bring back the prefab placeholder "LABEL".

### Text 0.1.0

- `GameText.AddRewriter`, `RefreshAll`, `TryGetSource`.

### Dialogue 0.1.0

- `GameDialogue.LineShowing`, `OptionShowing`, `NodeStarted`, `CurrentNode`, `TryGetLine`.

### Tool window 0.1.0

- One shared window (F1 by default, `[General] ToggleKey`) where mods add tabs with `ToolWindow.AddTab`. Frees the cursor, blocks game input under the window, turns gamepad and Steam Deck trackpad presses into clicks, and draws with a font that has Japanese and Chinese glyphs. A tab that throws is turned off with its error shown; the other tabs keep working.

### Assets 0.1.0

- `GameFonts`: one fallback font chain for the whole game, prepared per language at startup so Direct3D 12 does not crash. `GameAssets.LoadTexture` and `LoadBundle`, cached per file.

### Flags and saves 0.1.0

- `GameSaves`: save slots, level and flags, edits that snapshot first, restore, and a history of every version in `BepInEx/SaveHistory`. `GameFlags`: the flag catalog from CSV files.
