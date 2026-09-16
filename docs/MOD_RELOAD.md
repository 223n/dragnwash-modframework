# Mod hot reload (design)

[日本語](MOD_RELOAD.ja.md)

> **Experimental.** Built in the core (1.2.0), on `main` but not yet in a release. Where the build differs from the design: `UnloadOwned` became `ModReload.Unloading`, an event each library handles for its own registrations (by GUID where it kept one, else by the old build's assembly with `ModReload.Prune`); an Options row is not removed but re-pointed when the new build adds it again; the Mods screen does not yet say a mod was reloaded (`mods` in the Console does); and a build is delivered as `<Mod>.dll.new`, because on Windows the running DLL is locked and cannot be overwritten (the preloader makes the `.new` the real DLL at the next launch).

Building a mod, closing the game, starting it, clicking through the title screen, opening the shop: a minute per attempt, many attempts an hour. The Localization mod already reloads its *translation files* without a restart. This memo is about reloading the *mod itself*: after a build, the new DLL takes the place of the running one, while the game keeps running. A developer-tools feature (GUIDE rule 8), never something a player sees.

## What can and cannot be done

- **Mono never unloads an assembly.** A "reload" loads the new build next to the old one and stops the old one from doing anything. The old code stays in memory; during development that is a few hundred kilobytes per reload and does not matter. This is how BepInEx.Debug's ScriptEngine works too.
- **Mods (plugins): yes.** Everything a mod registers with the framework carries its GUID, so the old build can be taken out cleanly.
- **Libraries (the framework's Dialogue, Assets and so on): no.** Other mods reference a library's types directly; a new library build would leave those mods bound to the old types. Libraries need a restart, and the design does not try.
- **"Install or update a mod without restarting" for players: not now.** Same machinery, but a mod written on the assumption of "once, at startup" breaks, and the Direct3D 12 problem below would land on players. The current "please restart" flow is enough; reconsider once the developer feature has been stable for a while.

## How it works

1. **Watch.** While developer tools are on, the framework watches `BepInEx/plugins/<Mod>/*.dll` for the mods that opted in (below). A changed file fires after half a second of quiet, so a build's copy step has finished.
2. **Check the new build first.** Load the new DLL from bytes (the file stays unlocked for the next build) and find its `BaseUnityPlugin`. If that fails, print why in the Console and leave the old build running. Nothing is taken down that cannot be replaced.
3. **Take the old build out**, in this order:
   - `ModFramework.UnloadOwned(guid)`: everything registered with that GUID goes. Tool window tabs and commands, text rewriters, `GameEvents` handlers, `ModInfo`, services, `GameHooks` records, Options rows.
   - `Harmony.UnpatchAll(guid)`: the mod's own patches, which is why the mod must use its GUID as its Harmony ID.
   - `Destroy` the plugin's MonoBehaviour. Its `OnDestroy` does the mod's own clean-up.
   - Library hooks stay: they are shared.
4. **Start the new build.** `AddComponent` of the plugin type on the framework's own GameObject; BepInEx runs `Awake` as at startup. `Chainloader.PluginInfos` is not touched: the reloaded build is outside BepInEx's bookkeeping, and the Mods screen says so ("reloaded 3 times, not the file BepInEx loaded").
5. **Report.** One line in the Console: "Reloaded <Mod> (3rd time)". A failure is printed in red with the reason. `GameEvents.OnGameStarted` handlers of the new build run at once, as they do for any late registration.

### Direct3D 12

The new build's `Awake` runs mid-game. A mod that loads textures or prepares fonts there uploads them at exactly the moment Direct3D 12 may crash (Unity UUM-140564). Same protection as texture reloading: a marker file before the reload, removed after; a marker still there at the next start means the reload took the game down, and reloading is switched off until turned back on. The Console says to work with `-force-d3d11`.

## What a mod promises (to be added to GUIDE)

Only a mod that says it is reloadable is watched: `ModInfo.Reloadable = true` when registering, or a `[ReloadableMod]` attribute on the plugin class. A mod that says nothing is never reloaded. To keep the promise:

- Harmony instance ID = the mod's GUID (`new Harmony(MyMod.Guid)`), so `UnpatchAll(guid)` finds every patch.
- Register through the framework (`AddTab`, `AddRewriter`, `GameEvents`, `Services`, `GameOptions`) rather than holding things in static fields of your own; what the framework does not know about, it cannot take out.
- Static state is reset by the reload only if it lives in the new assembly's statics. Anything the old build handed to the game (a coroutine, a `DontDestroyOnLoad` object) must be cleaned up in `OnDestroy`.
- Nothing in `Awake` that is unsafe mid-game on Direct3D 12, or gate it with `GameFonts.RuntimeUploadsAreSafe`.

## Using it

- Console: `mods reload <guid>` by hand, `mods watch on|off` for the watcher; `mods` lists which mods are reloadable and how many times each was reloaded.
- A `.csproj` `Target` that copies the built DLL into `BepInEx/plugins/<Mod>/` after each build, in GUIDE as a copy-and-paste example.
- The Localization mod is the first reloadable mod and the proving ground: `UnloadOwned` is written against what it registers, and the design is adjusted from a few days of using it.

## What the framework needs first

| Piece | Now | Needed |
|---|---|---|
| Tool window tabs and commands | `IDisposable` per registration | a per-GUID sweep |
| Text rewriters | `IDisposable` per registration | a per-GUID sweep |
| `GameEvents` | `Remove(guid)` | done |
| `ModInfo`, `Services`, `GameHooks`, `GameOptions` | no removal | `UnloadOwned(guid)` removes them; a service's consumers keep the old instance until they ask again, so `Services.WhenAvailable` callbacks run again for the new one |
| Dialogue listeners, Saves | per registration | a per-GUID sweep |

## Order of work

1. `ModFramework.UnloadOwned(guid)` and the per-GUID sweeps in each library. Useful on its own: a mod switched off on the Mods screen can be taken out the same way.
2. Loading and swapping, and the Console commands.
3. The watcher.
4. GUIDE section and the `.csproj` example.
5. Localization opts in; use it for a week; then decide what else the design missed.

Not in this design: reloading libraries, reloading for players, reloading assets other than the DLL (textures already have their own; translation files too).
