# Making mods with Dungeonmans ModKit

ModKit loads code mods for Dungeonmans and gives them a small toolkit. Mods can be:
- **a folder of `.cs` files**, which the game compiles at startup (no Visual Studio, no .NET SDK); or
- **a compiled DLL**, if you prefer a full IDE.

Either way you get Harmony (to change any game method), events, UI helpers, a settings menu and saved data.

> Dungeonmans also has an **official** mod system (`modcontent\mods` in your save folder) for *data*: new items,
> monsters, powers and dungeons written as text EntityDefs. Use that for content. Use ModKit for things the official
> system can't do: changing existing game code, menus, UI and quality-of-life features. A project can ship both.

---

## 1. Your first mod in 2 minutes

1. In the game folder, create `Mods\MyFirstMod\code\`.
2. Create `Mods\MyFirstMod\mod.json`:
   ```json
   {
     "id": "my_first_mod",
     "name": "My First Mod",
     "version": "1.0.0",
     "author": "you",
     "description": "Says hello."
   }
   ```
3. Create `Mods\MyFirstMod\code\MyFirstMod.cs`:
   ```csharp
   public class MyFirstMod : IMod
   {
       public void Load(ModContext ctx)
       {
           Events.LevelEntered += (g, level) => Dm.Say("^3Hello^7 from my first mod!");
       }
   }
   ```
4. Start the game and enter any area. If nothing happens, open `ModKit\modkit.log`: compile errors are listed
   there with file and line.

A complete example (a button, settings, saved data, a Harmony patch) is in `ModKit\Examples\HelloAcademy`.
Copy that folder into `Mods\` to try it.

In `.cs` mods these namespaces are already imported: `System`, `System.Collections.Generic`, `System.Linq`,
`DungeonMans` (the game), `DungeonmansModKit`, `HarmonyLib` and `Microsoft.Xna.Framework`. The compiled result is
cached in the mod's `.cache` folder and rebuilt only when a `.cs` file, ModKit or the game changes.

---

## 2. Mod folder layout

```
Mods\
  MyMod\
    mod.json          id, name, version, author, description, minModKitVersion (all optional except id)
    code\*.cs         source files (compiled by ModKit), and/or
    MyMod.dll         a compiled mod (plus any extra DLLs it needs)
    config.json       created by ModKit: your mod's switch values
    (your files)      ctx.PathFor("myfile.json") gives the full path
```

- `id` must be unique. It prefixes your saved-data keys and your Harmony id. If you leave it out, the folder name
  is used.
- Mods load in alphabetical order of folder name.
- Players switch whole mods on and off in **Mod Settings**; that takes effect at the next launch.

---

## 3. The API

### `IMod` and `ModContext`
ModKit creates every public class implementing `IMod` and calls `Load(ctx)` once, before the game's `Main`
runs (no hero or level exists yet). Subscribe to events and register patches there.

| Member | What it is |
|---|---|
| `ctx.Info` | Your mod.json |
| `ctx.Folder`, `ctx.PathFor(file)` | Your mod folder |
| `ctx.Harmony` | A Harmony instance (`modkit.<id>`) |
| `ctx.Patch(name, h => ...)` | Runs a group of patches. Logs `ok` or `FAIL`; a failure doesn't stop your mod. Use one group per feature. |
| `ctx.Log.Write(msg)` | Writes `[id] msg` to `ModKit\modkit.log` |
| `ctx.Settings` | Switches in the Mod Settings menu (below) |
| `ctx.Data` | Values stored in the player's save (below) |

### Settings: `ctx.Settings`
```csharp
ctx.Settings.AddToggle("fast_doors", "Fast doors", "Doors open instantly.");   // shown in Mod Settings
if (ctx.Settings.On("fast_doors")) { ... }                                     // check it every time
ctx.Settings.Toggle("panel_expanded");                                         // unregistered keys: hidden, still saved
```
Read `On(...)` each time your code runs instead of once at load, so switching applies immediately.

### Saved data: `ctx.Data`
```csharp
int n = ctx.Data.GetInt("kills");     ctx.Data.SetInt("kills", n + 1);       // whole save: survives death and NG+
string s = ctx.Data.GetString("notes"); ctx.Data.SetString("notes", "...");
ctx.Data.SetHeroString("buff", "on"); ctx.Data.HeroString("buff");          // current hero only
```
Values are stored inside the game's own save, with keys prefixed `<id>_`. The game saves them whenever it saves.

### Events: `Events`
| Event | When |
|---|---|
| `DrawOverlay(dmGame g)` | Every frame (title screen and in game), just before the mouse cursor. Draw your windows here. |
| `Input(dmGame g) â†’ bool` | In game, before the game reads input. Return `true` to swallow this frame's input (e.g. while your window is open). |
| `MainMenuInput(dmGame g) â†’ bool` | The same, on the title screen. |
| `Update(dmGame g)` | Every update. |
| `LevelEntered(dmGame g, wgWorldData level)` | A new area became current (dungeon floor, town, Academy, overworld). |
| `HeroChanged(dmGame g, dmHero hero)` | A save was loaded or a new hero started. |
| `HeroDied(dmGame g, dmHero hero)` | The hero died. |
| `DialogShown(dmGame g, dmDialogBox box)` | A dialog is about to appear; you can edit it. `box.dialogInstigator` is the speaker. |

Exceptions in your handlers are caught and logged (the first 3 per handler), so they never crash the game.

### Game shortcuts: `Dm`
`Dm.Game`, `Dm.Hero`, `Dm.Renderer`, `Dm.InGame`, `Dm.InAcademy`, `Dm.OnOverworld`,
`Dm.Actors<T>()` (everything of a type on the current level: NPCs as `NPCAcademyHeadmaster`..., floor items as
`dmTreasure`, monsters as `dmMonster`), `Dm.Say(text)` (message log), `Dm.Visit(actor)` (teleport next to it and bump
into it, which opens its menu), `Dm.TravelTo(location, message)` (overworld auto-walk).

Text supports the game's color codes: `^3` yellow, `^1` red, `^5` blue, `^7` white (back to normal).

### UI: `Ui`
Immediate-mode helpers: call them every frame from `DrawOverlay`, between `g.theRenderer.BeginSpriteBatch()` and
`EndSpriteBatch()`. Clickable areas you stop drawing disappear by themselves.
```csharp
g.theRenderer.BeginSpriteBatch();
Ui.Window("mymod_win", 100, 100, 8, 3, "My window");      // parchment box, size in 32 px units; blocks clicks below
Ui.Text("Gold: ^3" + gold, 120, 150);
Rectangle b = Ui.Button("mymod_ok", 120, 180, "OK", () => open = false);
Ui.Button("mymod_cancel", b.Right + 12, 180, "Cancel", Close, enabled: canCancel);
g.theRenderer.EndSpriteBatch();
```
- `Ui.IsBusy(g)`: true when any game menu, dialog, map or mod window is open. Hide HUD-style windows when it's true.
- `Ui.AddModal(() => myWindowOpen)`: makes your window count in `IsBusy`, so other mods stay out of its way.
- Also: `Ui.Area(id, rect, onClick)` + `Ui.Hovered(frame)`, `Ui.WrappedText`, `Ui.DimAndBlock`, `Ui.ScreenSize`, `Ui.SetFont`.
- Prefix ids with your mod id (`mymod_...`); the game identifies clickable areas by id.

### Dialogs: `Dialogs`
```csharp
Dialogs.Show("Take the gold?", new[] { "Yes", "No" }, pick => { if (pick == 0) TakeGold(); });
Dialogs.Message("Done!");
// Remove an answer from a game dialog without breaking the NPC's code:
Events.DialogShown += (g, box) =>
{
    if (box.dialogInstigator is NPCAcademyHeadmaster)
        Dialogs.HideResponse(box, Dialogs.FindResponse(box, "books for the Library"));
};
```
NPC code reacts to answer *numbers*. `HideResponse` remaps the answers below the removed one, so the NPC still
receives the number it expects.

### Harmony: `Patching`
```csharp
ctx.Patch("Battlemaster greeting", h =>
    h.Patch(Patching.Method(typeof(NPCAcademyBattlemaster), nameof(NPCAcademyBattlemaster.EncroachedBy)),
            postfix: Patching.Hook(typeof(MyMod), nameof(BattlemasterPostfix))));

static void BattlemasterPostfix() => Dm.Say("The Battlemaster nods at you.");
```
`Patching.Method` throws a clear `MissingMethodException` when a game update renames something, and
`ctx.Patch` turns that into a `FAIL` line instead of a crash. See the Harmony docs
(https://harmony.pardeike.net) for prefixes, postfixes, `__instance`, `ref` arguments and transpilers.

---

## 4. Finding your way in the game's code

The game is `Dungeonmans.dll` in the game folder (C#, .NET 8, MonoGame, all in namespace `DungeonMans`).
Open it in **ILSpy** (free) or dnSpyEx to read the code. Useful starting points:

| Thing | Where |
|---|---|
| The game, global state | `dmGame` (`dmUtilities.theGame`): `dungeonMans` (hero), `theCurrentLevel`, `allActors`, `theRenderer`, `theInput`, `currentDialogBox`, lots of `b...Mode` flags |
| The hero | `dmHero` : `dmCreature` : `dmActor` |
| Account-wide progress | `dmGazetteer` (static), `dmGazetteer.academyProgress` (Academy wings, Proofs...) |
| Academy NPCs | `NPCAcademyHeadmaster`, `NPCAcademyBattlemaster`, `NPCAcademyHatch`... (`EncroachedBy` = bumped into, `ProcessDialogResponse` = an answer was picked) |
| Drawing | `wg2DRenderer`: `DrawString`, `DrawParchmentBox`, `DrawLine`, fonts `fontNyala18/24/30` |
| Clickable areas | `dmUIMouseFrameManager.AddFrame` (`g.theUIFrames`) |
| Input | `g.theInput.ExecutedActions` (game actions such as `ActionButton.Enter`, `MouseWheel_Up`), `g.theInput.PressedKeys` |
| Game data | `Content\` text files in the game folder (EntityDefs); the official mod system overrides these |

Tips:
- To find where something happens, search ILSpy for a piece of text you see on screen.
- Methods that are `private` can still be patched by name with Harmony, and read with `AccessTools`.
- Keep patches small and check your setting inside them, so players can switch them off.

---

## 5. DLL mods (optional)

If you prefer Visual Studio or Rider, make a .NET 8 class library:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Dungeonmans</GameDir>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Dungeonmans"><HintPath>$(GameDir)\Dungeonmans.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="MonoGame.Framework"><HintPath>$(GameDir)\MonoGame.Framework.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="DungeonmansModKit"><HintPath>$(GameDir)\ModKit\DungeonmansModKit.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="0Harmony"><HintPath>$(GameDir)\ModKit\0Harmony.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
</Project>
```
Put the built DLL and a `mod.json` in `Mods\YourMod\`. `ModKit\DungeonmansModKit.xml` gives IntelliSense
documentation for the whole API. [dungeonmans-QoL-megapack](https://github.com/chibranche/dungeonmans-QoL-megapack) is a full-size example of a DLL mod.

---

## 6. Testing and sharing

- **Log:** `ModKit\modkit.log` (the previous launch is kept as `modkit.prev.log`).
- **Game updates** can rename game methods. Your patches then show `FAIL` in the log and in Mod Settings, while
  the rest of your mod keeps working. Update your mod against the new game code.
- **Sharing:** zip your mod folder (without `.cache` and `config.json`). Players extract it into `Mods\`. Say in your
  description that ModKit is required, and link to it.
- **Two mods patching the same method** usually work together (Harmony chains patches). If they really conflict,
  check both mods' `FAIL` lines and the order they load in.
