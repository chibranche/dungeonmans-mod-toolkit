DUNGEONMANS MODKIT
==================

A mod loader for Dungeonmans (Steam, Windows). It loads every mod in the game's "Mods" folder and adds a
"Mod Settings" menu (title screen, and under the Options window) to switch mods and their features on and off.

INSTALL
1. Steam: right-click Dungeonmans > Manage > Browse local files. This opens the game folder.
2. Extract this zip into that folder (you get ModKit\, any mods' folders under Mods\, and these .bat files next to Dungeonmans.exe).
3. Close the game if it's running, then double-click ModKit-Install.bat.
4. Start the game. A "Mod Settings" button appears at the bottom left of the title screen.

AFTER A STEAM UPDATE
Steam updates (and "Verify integrity of game files") restore one game file, Dungeonmans.runtimeconfig.json,
which is how ModKit gets loaded. If your mods stop showing up, close the game and run ModKit-Install.bat again.

ADDING MODS
Each mod is a folder inside Mods\ (with a mod.json file). Drop it in and restart the game.
Mods are switched on/off in Mod Settings; whole-mod switches apply at the next launch.

MOVING OR REMOVING THE GAME
ModKit is registered with the full path of ModKit\DungeonmansModKit.dll. Before moving the game to another Steam
library or deleting the ModKit folder, run ModKit-Uninstall.bat (or Steam's "Verify integrity of game files"),
otherwise the game can't start.

SOMETHING WENT WRONG?
Open ModKit\modkit.log. Each mod lists every part as "ok" or "FAIL". A failing part just turns itself off;
the game and other mods keep working. Mod Settings also marks mods or parts that failed.

UNINSTALL
Double-click ModKit-Uninstall.bat. The game starts without mods. Delete ModKit\ and Mods\ if you want.

MAKING MODS
See ModKit\MODDING.md and the example in ModKit\Examples\HelloAcademy (a mod made of one .cs file, compiled by
the game itself: no Visual Studio needed).
