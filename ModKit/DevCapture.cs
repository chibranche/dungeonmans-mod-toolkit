using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DungeonMans;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonmansModKit;

/// <summary>
/// Developer tool for screenshots (documentation, checking layouts). Inactive unless ModKit\devcapture.txt exists:
/// then ModKit loads the first save slot, runs every listed scene (one name per line, or "all"), saves a PNG of the
/// screen per scene into ModKit\shots\ and quits WITHOUT saving. Mods register their scenes in Load; see
/// tools\capture.ps1 in the toolkit repo. Never active for players.
/// </summary>
public static class DevCapture
{
	private sealed class Shot
	{
		public string Name;
		public Action Setup, Cleanup;
	}

	private static readonly List<Shot> scenes = new List<Shot>();

	/// <summary>
	/// A screen to capture: setup opens it, cleanup (optional) closes it before the next scene. Prefix the name with
	/// your mod id (e.g. "qol_hub"); it becomes the file name.
	/// </summary>
	public static void Scene(string name, Action setup, Action cleanup = null) =>
		scenes.Add(new Shot { Name = name, Setup = setup, Cleanup = cleanup });

	/// <summary>True during a capture session (skip popups, tutorials...).</summary>
	public static bool Active { get; private set; }

	private static string ListPath => Path.Combine(Loader.KitDir, "devcapture.txt");
	private static string ShotsDir => Path.Combine(Loader.KitDir, "shots");

	private static List<Shot> queue;
	private static int stage; // 0 wait for menu, 1 loading, 2 settle, 3 scenes
	private static int wait;
	private static Shot current, previous;

	internal static void Start(Harmony h)
	{
		if (!File.Exists(ListPath))
		{
			return;
		}
		Active = true;
		string[] wanted = File.ReadAllLines(ListPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();
		queue = scenes.Where(s => wanted.Contains("all") || wanted.Contains(s.Name)).ToList();
		KitLog.Write("DevCapture active: " + string.Join(", ", queue.Select(s => s.Name)));
		Events.Update += Tick;
		h.Patch(Patching.Method(typeof(dmGame), "Draw"), postfix: Patching.Hook(typeof(DevCapture), nameof(DrawPostfix)));
	}

	private static void CloseDialog(dmGame g)
	{
		if (g.bDialogBoxMode)
		{
			g.currentDialogBox = null;
			g.bDialogBoxMode = false;
		}
	}

	private static void Tick(dmGame g)
	{
		switch (stage)
		{
		case 0:
			dmMainMenuInterface menu = g.mainMenu;
			if (g.bShowMainMenu && menu != null && menu.eMainMenuState == dmMainMenuInterface.MainMenuState.MMS_MainTitle && menu.loadableHeroes != null)
			{
				int slot = Array.FindIndex(menu.loadableHeroes, w => w?.activeHero != null);
				if (slot < 0)
				{
					Finish("no save with a hero");
					return;
				}
				loadableWorldData w = menu.loadableHeroes[slot];
				g.iSaveSlot = slot;
				g.selectedHeroToLoad = w;
				g.strPlayerName = w.activeHero.Name;
				g.strActiveWorldName = w.strWorldName;
				menu.InitMainMenu();
				g.BeginGameplay(bNewGame: false);
				stage = 1;
			}
			break;
		case 1:
			if (Dm.InGame && g.theCurrentLevel != null)
			{
				stage = 2;
				wait = 180;
			}
			break;
		case 2:
			CloseDialog(g);
			if (--wait <= 0)
			{
				stage = 3;
				NextScene();
			}
			break;
		case 3:
			CloseDialog(g);
			g.theInput.HideMouse(); // no pointer in the pictures
			wait--;
			break;
		}
	}

	private static void NextScene()
	{
		try
		{
			previous?.Cleanup?.Invoke();
		}
		catch (Exception e)
		{
			KitLog.Write("DevCapture cleanup of " + previous.Name + " failed: " + e.Message);
		}
		if (queue.Count == 0)
		{
			Finish("done");
			return;
		}
		current = queue[0];
		queue.RemoveAt(0);
		try
		{
			current.Setup();
		}
		catch (Exception e)
		{
			KitLog.Write("DevCapture scene " + current.Name + " setup failed: " + e);
		}
		wait = 40;
	}

	private static void DrawPostfix(dmGame __instance)
	{
		if (stage != 3 || current == null || wait > 0)
		{
			return;
		}
		try
		{
			GraphicsDevice gd = __instance.GraphicsDevice;
			int w = gd.PresentationParameters.BackBufferWidth, h = gd.PresentationParameters.BackBufferHeight;
			var data = new Color[w * h];
			gd.GetBackBufferData(data);
			for (int i = 0; i < data.Length; i++)
			{
				data[i].A = 255;
			}
			using var tex = new Texture2D(gd, w, h);
			tex.SetData(data);
			Directory.CreateDirectory(ShotsDir);
			using FileStream fs = File.Create(Path.Combine(ShotsDir, current.Name + ".png"));
			tex.SaveAsPng(fs, w, h);
			KitLog.Write("DevCapture: " + current.Name + ".png " + w + "x" + h);
		}
		catch (Exception e)
		{
			KitLog.Write("DevCapture capture failed: " + e);
		}
		previous = current;
		current = null;
		NextScene();
	}

	private static void Finish(string why)
	{
		KitLog.Write("DevCapture finished: " + why);
		// Hard exit: a capture session never saves anything.
		Environment.Exit(0);
	}
}
