# Dungeonmans ModKit

A mod loader and toolkit for [Dungeonmans](https://store.steampowered.com/app/288120/Dungeonmans/) (Steam, Windows).

- Loads every mod in `<game>\Mods\`: compiled DLLs, or **plain `.cs` files that the game compiles itself**, so no
  Visual Studio or .NET SDK is needed.
- Gives mods Harmony (change any game method), game events, UI helpers in the game's style, dialog helpers,
  saved data and per-mod settings.
- Adds a **Mod Settings** menu (title screen, and under the Options window) where players switch mods and their
  features on and off.
- If a mod breaks (for example after a game update), the part that can't load is logged and left off, and the game
  and other mods keep working. Errors while a patch runs are not caught: check `ModKit\modkit.log`.

Nothing in `Dungeonmans.dll` is modified: ModKit runs as a .NET startup hook.

## For players

1. Download `DungeonmansModKit-<version>.zip` from the Releases page.
2. Steam: right-click Dungeonmans > Manage > Browse local files, and extract the zip into that folder.
3. Close the game and double-click `ModKit-Install.bat`. Run it again after a Steam update.
4. Put mods in `Mods\`. Uninstall with `ModKit-Uninstall.bat`.

## For modders

Read **[docs/MODDING.md](docs/MODDING.md)**: a 2-minute first mod, the full API, how to find your way in the game's
code, and how to share your mod. A complete one-file example is in
[Examples/HelloAcademy](Examples/HelloAcademy/code/HelloAcademy.cs). Mods built on ModKit:
[QoL megapack](https://github.com/chibranche/dungeonmans-QoL-megapack),
[Poultry Coop](https://github.com/chibranche/dungeonmans-poultry-coop),
[Pay 2 Lose Metaprogression](https://github.com/chibranche/dungeonmans-pay2lose-metaprogression).

```csharp
public class MyFirstMod : IMod
{
    public void Load(ModContext ctx)
    {
        ctx.Settings.AddToggle("hello", "Say hello", "Greets you in every new area.");
        Events.LevelEntered += (g, level) =>
        {
            if (ctx.Settings.On("hello")) Dm.Say("^3Hello^7 from my first mod!");
        };
    }
}
```

## Repository

| Path | What |
|---|---|
| [ModKit/](ModKit/) | The loader and API (`DungeonmansModKit.dll`) |
| [Examples/](Examples/) | Example mods |
| [docs/MODDING.md](docs/MODDING.md) | Modder guide and API reference |
| [tools/](tools/) | Player installer (`Install-ModKit.ps1` and the `.bat` files) and player README |

Building needs the .NET 8 SDK and the game installed (it's referenced from the game folder):

```powershell
.\install.ps1 [-GameDir <path>] [-WithExample]   # build, install into <game>\ModKit, register the hook
.\install.ps1 -Uninstall                         # remove the hook
.\build-release.ps1                              # dist\DungeonmansModKit-<v>.zip
```

How it loads: ModKit is registered as `STARTUP_HOOKS` in `Dungeonmans.runtimeconfig.json`, so .NET runs it before the
game's `Main`. Steam updates restore that file, which is why players re-run the installer after one.

Developed against Dungeonmans 1.14.4.9 (Steam build 20296537).

## License

MIT, see [LICENSE](LICENSE).
