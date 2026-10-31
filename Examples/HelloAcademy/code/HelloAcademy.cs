// Example ModKit mod written as a plain .cs file: no Visual Studio needed. ModKit compiles every .cs file in
// this "code" folder when the game starts (errors go to ModKit\modkit.log with file and line).
// Already available without "using": System, System.Linq, System.Collections.Generic, DungeonMans (the game),
// DungeonmansModKit, HarmonyLib and Microsoft.Xna.Framework.

namespace HelloAcademy;

public class HelloAcademyMod : IMod
{
	private static ModContext ctx;

	public void Load(ModContext context)
	{
		ctx = context;

		// 1. Switches: shown in the Mod Settings menu under this mod's name. Read them with Settings.On(...).
		ctx.Settings.AddToggle("button", "Show 'Visit Headmaster' button", "A button in the Academy that takes you to the Headmaster.");
		ctx.Settings.AddToggle("greet", "Greet on entering a level", "Writes a line in the message log each time you enter a new area.");

		// 2. Events: ModKit calls these for you. A handler that throws is logged and can't crash the game.
		Events.DrawOverlay += Draw;
		Events.LevelEntered += (g, level) =>
		{
			if (ctx.Settings.On("greet"))
			{
				// Saved data survives deaths and NG+ (stored in the save, keys prefixed with the mod id).
				int visits = ctx.Data.GetInt("levels_entered") + 1;
				ctx.Data.SetInt("levels_entered", visits);
				Dm.Say("^3Hello Academy:^7 entered " + (level.levelName ?? "a new area") + " (area #" + visits + " this save).");
			}
		};

		// 3. Harmony: patch any game method. ctx.Patch logs "ok"/"FAIL" and keeps the mod running if it fails.
		ctx.Patch("Battlemaster greeting", h =>
			h.Patch(Patching.Method(typeof(NPCAcademyBattlemaster), nameof(NPCAcademyBattlemaster.EncroachedBy)),
				postfix: Patching.Hook(typeof(HelloAcademyMod), nameof(BattlemasterPostfix))));
	}

	private static void BattlemasterPostfix()
	{
		Dm.Say("^3Hello Academy:^7 the Battlemaster nods at you.");
	}

	private static void Draw(dmGame g)
	{
		// Only in the Academy, only when no menu/dialog/mod window is open.
		if (!ctx.Settings.On("button") || !Dm.InAcademy || Ui.IsBusy(g))
		{
			return;
		}
		g.theRenderer.BeginSpriteBatch();
		Ui.Window("hello_window", 16, 120, 7, 1);
		Ui.Button("hello_visit", 32, 132, "Visit the Headmaster", VisitHeadmaster);
		g.theRenderer.EndSpriteBatch();
	}

	private static void VisitHeadmaster()
	{
		NPCAcademyHeadmaster hm = Dm.Actors<NPCAcademyHeadmaster>().FirstOrDefault();
		if (hm == null || !Dm.Visit(hm))
		{
			Dialogs.Message("I can't find a way to the Headmaster right now.");
		}
	}
}
