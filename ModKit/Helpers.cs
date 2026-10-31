using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DungeonMans;
using HarmonyLib;
using Microsoft.Xna.Framework;

namespace DungeonmansModKit;

/// <summary>Small Harmony helpers that fail loudly (with the missing name) instead of patching null.</summary>
public static class Patching
{
	/// <summary>The game method to patch. Throws MissingMethodException if it doesn't exist (e.g. after a game update).</summary>
	public static MethodInfo Method(Type type, string name, Type[] parameters = null) =>
		AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name);

	/// <summary>Your prefix/postfix/transpiler method, wrapped for Harmony.Patch.</summary>
	public static HarmonyMethod Hook(Type type, string name) =>
		new HarmonyMethod(AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name));
}

/// <summary>Shortcuts for common game state and actions.</summary>
public static class Dm
{
	public static dmGame Game => dmUtilities.theGame;
	public static dmHero Hero => Game?.dungeonMans;
	public static wg2DRenderer Renderer => Game?.theRenderer;

	/// <summary>A hero is loaded and we're not on the title screen.</summary>
	public static bool InGame => Game?.dungeonMans != null && !Game.bShowMainMenu;
	public static bool OnOverworld => InGame && Hero.bIsInOverworld;
	public static bool InAcademy => InGame && !Hero.bIsInOverworld && Game.theCurrentLevel is dmAcademy;

	/// <summary>All actors of a type on the current level (NPCs, chests, floor items as dmTreasure, monsters...).</summary>
	public static IEnumerable<T> Actors<T>() where T : dmActor =>
		Game?.allActors?.OfType<T>().ToList() ?? Enumerable.Empty<T>();

	/// <summary>Writes a line in the game's message log. Supports the game's color codes (^3 yellow, ^1 red, ^7 white...).</summary>
	public static void Say(string message) => Game?.AddStringToLog(message);

	private static readonly (int dx, int dy)[] Around = { (0, 1), (1, 0), (-1, 0), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) };

	/// <summary>
	/// Teleports the hero next to an actor on the current level and bumps into it, which opens its menu or dialog.
	/// Returns false if there's no free tile next to it.
	/// </summary>
	public static bool Visit(dmActor actor)
	{
		dmGame g = Game;
		dmHero hero = g.dungeonMans;
		LocValue target = actor.Location;
		LocValue from = hero.Location;
		if (Math.Max(Math.Abs(from.X - target.X), Math.Abs(from.Y - target.Y)) != 1)
		{
			bool placed = false;
			foreach (var (dx, dy) in Around)
			{
				int x = target.X + dx, y = target.Y + dy;
				if (g.theCurrentLevel.GetTile(x, y) != null && g.theCurrentLevel.CheckPotentialHeroPosition(x, y, g))
				{
					from = new LocValue(x, y);
					placed = true;
					break;
				}
			}
			if (!placed)
			{
				return false;
			}
			g.TeleportHeroToLocation(from, bDontResetAP: true);
		}
		hero.UpdatePositionBasedOnPlayerInput(target.X - from.X, target.Y - from.Y, g, false, false);
		return true;
	}

	/// <summary>On the overworld: auto-walk to a location (turns pass and encounters can interrupt, like normal travel).</summary>
	public static void TravelTo(LocValue overworldLocation, string message) => Hero?.HeroAutoPathToLocation(overworldLocation, message);
}

/// <summary>Font sizes of the game's UI font.</summary>
public enum UiFont
{
	Small,
	Medium,
	Large,
}

/// <summary>
/// Immediate-mode UI helpers on top of the game's renderer and mouse frames. Call them every frame from
/// <see cref="Events.DrawOverlay"/>, between renderer.BeginSpriteBatch() and EndSpriteBatch(). Clickable areas
/// you stop drawing disappear on their own the next frame. Use ids prefixed with your mod id ("mymod_ok").
/// </summary>
public static partial class Ui
{
	/// <summary>Incremented once per drawn frame.</summary>
	public static int Frame { get; internal set; }

	/// <summary>Stack level used for mod windows; higher draws/clicks above lower.</summary>
	public const int WindowLevel = 20;

	private static readonly Dictionary<string, int> lastDrawn = new Dictionary<string, int>();
	private static readonly Dictionary<string, Action> clicks = new Dictionary<string, Action>();
	private static readonly List<Func<bool>> modals = new List<Func<bool>>();

	/// <summary>Registers "my window is open" so <see cref="IsBusy"/> (and other mods) treat it like a game menu.</summary>
	public static void AddModal(Func<bool> isOpen) => modals.Add(isOpen);

	/// <summary>True when any game menu, dialog, map, inventory, death screen or registered mod window is open.</summary>
	public static bool IsBusy(dmGame g)
	{
		if (SettingsPanel.IsOpen || modals.Any(m => SafeModal(m)))
		{
			return true;
		}
		return g.GetFullScreenMenuFlavor() != 0 || g.bGameMenuIsOpen || g.bShowMainMenu || g.bDialogBoxMode || g.bShowMasteriesMode
			|| g.bShopMode || g.bArmoryMode || g.bSpendProofsMode || g.bContextMenuOpen || g.bOptionsMode || g.bSelectInventoryItemForEffectMode
			|| g.bPay2LoseMenuMode || g.bShowPetMenuMode || g.bMuseumItemMode || g.bMonsterKnowledgeMuseumMode || g.bCartographyDeskMode
			|| g.bMoveShardsMode || g.bRenownItemUIMode || g.bDrawDeathLog || g.bDrawFullCombatLog || g.bShowPetChromificationMode
			|| g.bTowerManagerUIMode || g.bKeybindsUIMode || g.bMansiversaryCoinExchangeMode || g.bChooseOverworldGenerationMode
			|| g.currentSongAndDance == SongAndDanceType.player_died || g.theRenderer.activeScrollboxes.Count > 0
			|| g.bInventoryMode || g.bHelpMode || g.bQuestLogMode || g.bAreaMapMode || g.bDailyDangerMenuMode || g.bTargetingMode;
	}

	private static bool SafeModal(Func<bool> m)
	{
		try
		{
			return m();
		}
		catch
		{
			return false;
		}
	}

	private static bool StillDrawn(ui_mouseFrame f) => lastDrawn.TryGetValue(f.strFrameID, out int n) && n >= Frame - 1;

	private static object Route(ui_mouseFrame f, int button)
	{
		if (clicks.TryGetValue(f.strFrameID, out Action a))
		{
			try
			{
				a();
			}
			catch (Exception e)
			{
				KitLog.Write("Click on " + f.strFrameID + " threw: " + e);
			}
		}
		return true;
	}

	/// <summary>A clickable (or hover-only, if onClick is null) area. Returns the game's frame; use <see cref="Hovered"/>.</summary>
	public static ui_mouseFrame Area(string id, Rectangle r, Action onClick, int stackLevel = WindowLevel) => Area(id, r, onClick, null, stackLevel);

	/// <summary>A clickable area with a right-click action too.</summary>
	public static ui_mouseFrame Area(string id, Rectangle r, Action onClick, Action onRightClick, int stackLevel = WindowLevel)
	{
		ui_mouseFrame f = Dm.Game.theUIFrames.AddFrame(r.X, r.Y, r.Width, r.Height, stackLevel, id);
		lastDrawn[id] = Frame;
		f.RemoveIfFalse = StillDrawn;
		f.funcOnClick = null;
		f.funcOnRightClick = null;
		clicks.Remove(id);
		rightClicks.Remove(id);
		if (onClick != null)
		{
			clicks[id] = onClick;
			f.funcOnClick = Route;
		}
		if (onRightClick != null)
		{
			rightClicks[id] = onRightClick;
			f.funcOnRightClick = RouteRight;
		}
		return f;
	}

	private static readonly Dictionary<string, Action> rightClicks = new Dictionary<string, Action>();

	private static object RouteRight(ui_mouseFrame f, int button)
	{
		if (rightClicks.TryGetValue(f.strFrameID, out Action a))
		{
			try
			{
				a();
			}
			catch (Exception e)
			{
				KitLog.Write("Right-click on " + f.strFrameID + " threw: " + e);
			}
		}
		return true;
	}

	/// <summary>Is the mouse over this frame (and it's the top frame under the mouse)?</summary>
	public static bool Hovered(ui_mouseFrame f) => Dm.Game.theInput.IsActiveFrame(f);

	public static void SetFont(UiFont font)
	{
		wg2DRenderer tr = Dm.Renderer;
		tr.SetActiveFont(font == UiFont.Large ? tr.fontNyala30 : font == UiFont.Medium ? tr.fontNyala24 : tr.fontNyala18);
	}

	/// <summary>Text with the game's color codes (^3 yellow, ^1 red, ^5 blue, ^7 white...).</summary>
	public static void Text(string text, int x, int y, Color? color = null, UiFont font = UiFont.Small)
	{
		SetFont(font);
		Dm.Renderer.DrawString(text, new Vector2(x, y), color ?? Color.White);
	}

	/// <summary>Word-wrapped text; returns nothing, wraps at x + width.</summary>
	public static void WrappedText(string text, int x, int y, int width, UiFont font = UiFont.Small)
	{
		SetFont(font);
		Dm.Renderer.DrawWordWrappedString(text, new Vector2(x, y), x + width);
	}

	/// <summary>
	/// A text button: "[label]" in cyan, yellow when hovered, gray when disabled. Width is measured from the text
	/// unless given. Returns the button's rectangle so you can lay out the next one.
	/// </summary>
	public static Rectangle Button(string id, int x, int y, string label, Action onClick, bool enabled = true,
		int width = 0, UiFont font = UiFont.Small, int stackLevel = WindowLevel)
	{
		SetFont(font);
		string shown = "[" + label + "]";
		int w = width > 0 ? width : (int)Dm.Renderer.GetActiveFont().MeasureString(shown).X + 4;
		int h = font == UiFont.Large ? 34 : font == UiFont.Medium ? 28 : 22;
		var r = new Rectangle(x, y, w, h);
		ui_mouseFrame f = Area(id, r, enabled ? onClick : null, stackLevel);
		Color c = !enabled ? Color.DarkGray : Hovered(f) ? ShepColor.Yellow : ShepColor.Cyan;
		Dm.Renderer.DrawString(shown, new Vector2(x, y + 1), c);
		return r;
	}

	/// <summary>
	/// A parchment window in the game's style, sized in 32-pixel units, with an optional title.
	/// Also adds a click-catcher under it so clicks don't reach the game. Returns the window's rectangle.
	/// </summary>
	public static Rectangle Window(string id, int x, int y, int widthUnits, int heightUnits, string title = null, int stackLevel = WindowLevel)
	{
		wg2DRenderer tr = Dm.Renderer;
		var r = new Rectangle(x, y, widthUnits * 32, heightUnits * 32 + 32);
		Area(id + "_bg", r, null, stackLevel - 1);
		tr.DrawParchmentBox(x, y, widthUnits, heightUnits);
		if (title != null)
		{
			Text(title, x + 20, y + 10, ShepColor.Yellow, UiFont.Medium);
		}
		return r;
	}

	/// <summary>Darkens the whole screen and blocks clicks behind a modal window.</summary>
	public static void DimAndBlock(string id, int stackLevel = WindowLevel)
	{
		wg2DRenderer tr = Dm.Renderer;
		tr.DimScreen();
		Area(id + "_blocker", new Rectangle(0, 0, (int)tr.fWindowXSize, (int)tr.fWindowYSize), null, stackLevel - 1);
	}

	/// <summary>Screen size in pixels.</summary>
	public static Point ScreenSize => new Point((int)Dm.Renderer.fWindowXSize, (int)Dm.Renderer.fWindowYSize);
}

/// <summary>Dialog box helpers.</summary>
public static class Dialogs
{
	// For a dialog with hidden answers: visible index -> the index the game originally gave that answer.
	private static readonly ConditionalWeakTable<dmDialogBox, List<int>> remaps = new ConditionalWeakTable<dmDialogBox, List<int>>();

	/// <summary>
	/// Removes one answer from a dialog box before it's shown (call from <see cref="Events.DialogShown"/> or a postfix
	/// on the method that builds it). When the player picks an answer below it, the game still receives the original
	/// answer number, so the NPC's code keeps working. Indexes are the ones currently visible (0 = first).
	/// </summary>
	public static void HideResponse(dmDialogBox box, int visibleIndex)
	{
		if (box == null || visibleIndex < 0 || visibleIndex >= box.iNumResponses)
		{
			return;
		}
		List<int> map = remaps.GetValue(box, b => Enumerable.Range(0, b.iNumResponses).ToList());
		map.RemoveAt(visibleIndex);
		string[] shown = box.dialogResponses;
		for (int i = visibleIndex; i < shown.Length - 1; i++)
		{
			shown[i] = shown[i + 1];
		}
		shown[shown.Length - 1] = null;
		box.iNumResponses--;
	}

	/// <summary>Finds an answer by (part of) its text, ignoring color codes. -1 if not found.</summary>
	public static int FindResponse(dmDialogBox box, string textPart)
	{
		for (int i = 0; i < box.iNumResponses; i++)
		{
			if (box.dialogResponses[i] != null && box.dialogResponses[i].Contains(textPart, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	// Prefix on dmDialogBox.ProcessDialogResponse (registered by ModKit).
	internal static void RemapResponse(dmDialogBox __instance, ref int iResponseNum)
	{
		if (iResponseNum >= 0 && remaps.TryGetValue(__instance, out List<int> map) && iResponseNum < map.Count)
		{
			iResponseNum = map[iResponseNum];
		}
	}

	/// <summary>
	/// Shows a dialog with your own answers. onPick receives the chosen answer index (-1 if closed with Escape).
	/// The speaker's portrait is shown if given (defaults to the hero).
	/// </summary>
	public static void Show(string text, string[] responses, Action<int> onPick = null, dmActor speaker = null, bool small = false)
	{
		dmGame g = Dm.Game;
		var box = new dmDialogBox(speaker ?? g.dungeonMans, text, responses)
		{
			bUseSmallBox = small,
			onReceiveInput = (i, b) => onPick?.Invoke(i),
		};
		g.SpawnDialog(box);
	}

	/// <summary>A simple message with an OK button.</summary>
	public static void Message(string text, dmActor speaker = null) => Show(text, new[] { "OK" }, null, speaker, small: true);
}
