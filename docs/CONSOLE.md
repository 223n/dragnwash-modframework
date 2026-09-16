# Console

[日本語](CONSOLE.ja.md)

> **Experimental.** In the Tool window library from 1.1.0, on `main` but not yet in a release.

The **Console** tab of the Tool window (F1) shows the log as it happens, in colour, and takes commands. It is for people who make or debug mods; players never need it.

## The log

Every line BepInEx logs, from any mod and from Unity, arrives with its level and its source (the log source's name, such as `DragNWash.ModFramework.Assets` or `Unity Log`). The last 2,000 lines are kept. `BepInEx/LogOutput.log` still gets everything; the console only chooses what to show.

| Level | Colour | Typical use |
|---|---|---|
| Fatal, Error | red | crashes, exceptions |
| Warning | amber | a replacement clash, an unreadable file, the Direct3D 12 notice |
| Message | accent | results meant for a person ("Replacements applied in 3 places") |
| Info | normal | ordinary progress |
| Debug | muted | detail; hidden by default |

When an error arrives while the tab is not open, the tab button reads **Console !** until it is opened.

## Choosing what to see

Three ways, all writing the same settings, which are kept in the Tool window's config file and so survive a restart:

- **Buttons on the tab**: one per level to show or hide it, and a box that filters by source.
- **Commands**: `log show debug`, `log show info off`, `log level unity info`, `log level DragNWash.ModFramework.Assets debug`, `log level default warning`, `log filter Assets`.
- **The Mods screen**: Options → Mods → Drag'n Wash ModFramework: Tool window → `[Console] Show` and `[Console] Levels`.

Defaults: mods show Info and above, Unity's own log shows Warning and above, Debug is hidden. Hiding errors is possible; the tab then says so, so nothing is missed by accident.

## Commands

Type in the line at the bottom and press Enter. While you type, what could come next is listed above the line: command names for the first word, then what the command offers for its arguments (levels and sources for `log`, `textures` / `reload` and so on for `assets`). **Tab** fills in the highlighted one, up and down move through the list, Escape hides it; with no list, up and down walk the history. Built in:

| Command | Does |
|---|---|
| `help`, `help <name>` | lists commands, or describes one |
| `log <n>` | the last n lines |
| `log show <level> [off]`, `log level <source|unity|default> <level>`, `log filter <text>`, `log clear` | see above |
| `mods` | loaded plugins, with the features the framework found unavailable |
| `scene` | the loaded scene |
| `clear`, `cls` | clears the console (same as `log clear`) |
| `assets textures [filter]`, `assets replacements`, `assets apply`, `assets reload` | from the Assets library; see [ASSET_TOOL.md](ASSET_TOOL.md) |

A command that throws prints the error in red with the mod that owns it, and nothing else happens. There is no scripting language and none is planned: commands are what mods register.

## For mod authors

```csharp
ToolWindow.AddCommand(MyGuid, "tl", "tl reload | tl find <text>", args =>
{
    if (args.Length > 0 && args[0] == "reload") { Reload(); return "Reloaded."; }
    return "tl reload | tl find <text>";
});
```

- Pass a fifth argument to offer completions: it gets the words typed after the name (the last one partial, or "" right after a space) and returns what could stand there.
- One lower-case word for the name. When another mod registered the same name first, yours runs only as `yourguid:name` (the short form after the last dot also works), and `help` lists both.
- Return what to print; `\n` separates lines. Anything a person should not act on without care (changing a save, for example) should ask for a `--yes` argument.
- Print from elsewhere with `ConsoleLog.Print(text, LogLevel)`. Your normal BepInEx logging already appears with your mod's name.
- Keep command text ASCII, or call `ToolWindow.PrepareCharacters` for what you print (the window's font is rasterised at startup).
