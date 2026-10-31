using System;
using System.Collections.Generic;
using DungeonMans;
using HarmonyLib;

namespace DungeonmansModKit;

/// <summary>
/// Game events mods can subscribe to (usually in <see cref="IMod.Load"/>). Every handler runs inside its own
/// try/catch: a throwing handler is logged (first few times) and never crashes the game or other mods.
/// </summary>
public static class Events
{
	/// <summary>
	/// Every frame, on the main menu and in game, just before the mouse cursor is drawn. Draw your windows here
	/// (call renderer.BeginSpriteBatch()/EndSpriteBatch() yourself, or use <see cref="Ui"/> helpers inside them).
	/// Check <see cref="Dm.InGame"/> if you only draw in game.
	/// </summary>
	public static event Action<dmGame> DrawOverlay;

	/// <summary>
	/// In game, before the game reads this frame's input. Return true to consume it (the game then ignores this
	/// frame's keys/buttons), e.g. while your window is open. The keys are in g.theInput.PressedKeys and the
	/// game's actions in g.theInput.ExecutedActions.
	/// </summary>
	public static event Func<dmGame, bool> Input;

	/// <summary>On the main menu, before the menu reads input. Return true to consume it.</summary>
	public static event Func<dmGame, bool> MainMenuInput;

	/// <summary>Every game update (after the game's own update), main menu included.</summary>
	public static event Action<dmGame> Update;

	/// <summary>A new level/area became current (dungeon floor, town, Academy, overworld...).</summary>
	public static event Action<dmGame, wgWorldData> LevelEntered;

	/// <summary>A different hero became active: a save was loaded or a new hero started.</summary>
	public static event Action<dmGame, dmHero> HeroChanged;

	/// <summary>The hero just died (the death screen is starting).</summary>
	public static event Action<dmGame, dmHero> HeroDied;

	/// <summary>
	/// A dialog box is about to be shown. You can change its text or responses here
	/// (see <see cref="Dialogs.HideResponse"/> to remove an answer safely). box.dialogInstigator is who is talking.
	/// </summary>
	public static event Action<dmGame, dmDialogBox> DialogShown;

	// ---------- dispatch ----------

	private static readonly Dictionary<Delegate, int> errorCounts = new Dictionary<Delegate, int>();

	private static void Fail(Delegate d, string evt, Exception e)
	{
		errorCounts.TryGetValue(d, out int n);
		errorCounts[d] = ++n;
		if (n <= 3)
		{
			KitLog.Write($"Event {evt} handler {d.Method.DeclaringType?.FullName}.{d.Method.Name} threw{(n == 3 ? " (further errors hidden)" : "")}: {e}");
		}
	}

	private static void Raise<T>(Action<T> evt, string name, T a)
	{
		if (evt == null)
		{
			return;
		}
		foreach (Action<T> d in evt.GetInvocationList())
		{
			try
			{
				d(a);
			}
			catch (Exception e)
			{
				Fail(d, name, e);
			}
		}
	}

	private static void Raise<T1, T2>(Action<T1, T2> evt, string name, T1 a, T2 b)
	{
		if (evt == null)
		{
			return;
		}
		foreach (Action<T1, T2> d in evt.GetInvocationList())
		{
			try
			{
				d(a, b);
			}
			catch (Exception e)
			{
				Fail(d, name, e);
			}
		}
	}

	private static bool RaiseConsumable(Func<dmGame, bool> evt, string name, dmGame g)
	{
		if (evt == null)
		{
			return false;
		}
		foreach (Func<dmGame, bool> d in evt.GetInvocationList())
		{
			try
			{
				if (d(g))
				{
					return true;
				}
			}
			catch (Exception e)
			{
				Fail(d, name, e);
			}
		}
		return false;
	}

	internal static void OnDrawOverlay(dmGame g) => Raise(DrawOverlay, nameof(DrawOverlay), g);
	internal static bool OnInput(dmGame g) => RaiseConsumable(Input, nameof(Input), g);
	internal static bool OnMainMenuInput(dmGame g) => RaiseConsumable(MainMenuInput, nameof(MainMenuInput), g);
	internal static void OnDialogShown(dmGame g, dmDialogBox box) => Raise(DialogShown, nameof(DialogShown), g, box);

	private static wgWorldData lastLevel;
	private static dmHero lastHero;
	private static bool wasDead;

	internal static void OnUpdate(dmGame g)
	{
		if (g.dungeonMans != lastHero)
		{
			lastHero = g.dungeonMans;
			wasDead = false;
			if (lastHero != null)
			{
				Raise(HeroChanged, nameof(HeroChanged), g, lastHero);
			}
		}
		if (g.theCurrentLevel != lastLevel)
		{
			lastLevel = g.theCurrentLevel;
			if (lastLevel != null && g.dungeonMans != null)
			{
				Raise(LevelEntered, nameof(LevelEntered), g, lastLevel);
			}
		}
		bool dead = g.dungeonMans != null && g.currentSongAndDance == SongAndDanceType.player_died;
		if (dead && !wasDead)
		{
			Raise(HeroDied, nameof(HeroDied), g, g.dungeonMans);
		}
		wasDead = dead;
		Raise(Update, nameof(Update), g);
	}
}

/// <summary>The few Harmony patches ModKit itself needs to drive <see cref="Events"/> and the Mod Settings menu.</summary>
internal static class CoreHooks
{
	public static void Register(Harmony h)
	{
		h.Patch(Patching.Method(typeof(wg2DRenderer), nameof(wg2DRenderer.DrawMouse)),
			prefix: Patching.Hook(typeof(CoreHooks), nameof(DrawMousePrefix)));
		h.Patch(Patching.Method(typeof(dmGame), "CheckControllerInputForGame"),
			prefix: Patching.Hook(typeof(CoreHooks), nameof(GameInputPrefix)));
		h.Patch(Patching.Method(typeof(dmMainMenuInterface), nameof(dmMainMenuInterface.UpdateMainMenu)),
			prefix: Patching.Hook(typeof(CoreHooks), nameof(MainMenuInputPrefix)));
		h.Patch(Patching.Method(typeof(dmGame), "Update"),
			postfix: Patching.Hook(typeof(CoreHooks), nameof(UpdatePostfix)));
		h.Patch(AccessTools.Method(typeof(dmGame), nameof(dmGame.SpawnDialog), new[] { typeof(dmDialogBox), typeof(float) })
				?? throw new MissingMethodException("dmGame", "SpawnDialog(dmDialogBox, float)"),
			prefix: Patching.Hook(typeof(CoreHooks), nameof(SpawnDialogPrefix)));
		h.Patch(Patching.Method(typeof(dmDialogBox), nameof(dmDialogBox.ProcessDialogResponse)),
			prefix: Patching.Hook(typeof(Dialogs), nameof(Dialogs.RemapResponse)));
	}

	private static void DrawMousePrefix()
	{
		dmGame g = dmUtilities.theGame;
		if (g?.theRenderer == null)
		{
			return;
		}
		Ui.Frame++;
		Events.OnDrawOverlay(g);
		SettingsPanel.Draw(g);
	}

	private static bool GameInputPrefix(dmGame __instance)
	{
		if (SettingsPanel.IsOpen)
		{
			SettingsPanel.HandleInput(__instance);
			return false;
		}
		return !Events.OnInput(__instance);
	}

	private static void MainMenuInputPrefix(dmGame theGame)
	{
		if (SettingsPanel.IsOpen)
		{
			SettingsPanel.HandleInput(theGame);
		}
		else if (Events.OnMainMenuInput(theGame))
		{
			theGame.theInput.ExecutedActions.Clear();
		}
	}

	private static void UpdatePostfix(dmGame __instance) => Events.OnUpdate(__instance);

	private static void SpawnDialogPrefix(dmGame __instance, dmDialogBox newBox)
	{
		if (newBox != null)
		{
			Events.OnDialogShown(__instance, newBox);
		}
	}
}
