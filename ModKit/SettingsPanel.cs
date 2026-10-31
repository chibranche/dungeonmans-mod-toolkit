using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMans;
using Microsoft.Xna.Framework;

namespace DungeonmansModKit;

// "Mod Settings": every mod in the Mods folder, each with an enable switch (applies at next launch) and its own
// switches (apply immediately). Opened from a button on the title screen and under the Options window.
internal static class SettingsPanel
{
	public static bool IsOpen;
	private static int selected;
	private static int scroll;

	private sealed class Row
	{
		public LoadedMod Mod;
		public ModToggle Toggle; // null = the mod's header row
	}

	private static List<Row> rows = new List<Row>();

	private static void Rebuild()
	{
		rows = new List<Row>();
		foreach (LoadedMod m in Loader.Mods)
		{
			rows.Add(new Row { Mod = m });
			if (m.Context != null && m.State == "loaded")
			{
				rows.AddRange(m.Context.Settings.Toggles.Select(t => new Row { Mod = m, Toggle = t }));
			}
		}
	}

	private static dmGame Game => dmUtilities.theGame;

	// ---------- input ----------

	// Called with the game's executed actions for this frame; always consumes them.
	public static void HandleInput(dmGame g)
	{
		List<ActionButton> a = g.theInput.ExecutedActions;
		if (a.Contains(ActionButton.Close))
		{
			Close();
		}
		else if (a.Contains(ActionButton.Up) || a.Contains(ActionButton.ArrowKey_Up))
		{
			Select(selected - 1);
		}
		else if (a.Contains(ActionButton.Down) || a.Contains(ActionButton.ArrowKey_Down))
		{
			Select(selected + 1);
		}
		else if (a.Contains(ActionButton.MouseWheel_Up))
		{
			scroll = Math.Max(0, scroll - 3);
		}
		else if (a.Contains(ActionButton.MouseWheel_Down))
		{
			scroll = Math.Max(0, Math.Min(rows.Count - visibleRows, scroll + 3));
		}
		else if (a.Contains(ActionButton.Enter) || a.Contains(ActionButton.Select)
			|| a.Contains(ActionButton.Left) || a.Contains(ActionButton.Right))
		{
			Toggle(selected);
		}
		a.Clear();
	}

	private static void Select(int i)
	{
		if (rows.Count == 0)
		{
			return;
		}
		selected = Math.Clamp(i, 0, rows.Count - 1);
		if (selected < scroll)
		{
			scroll = selected;
		}
		else if (selected >= scroll + visibleRows)
		{
			scroll = selected - visibleRows + 1;
		}
	}

	private static void Open()
	{
		Rebuild();
		IsOpen = true;
		selected = 0;
		scroll = 0;
		dmAudio.PlayUIFX("open_dialog");
	}

	private static void Close()
	{
		IsOpen = false;
		dmAudio.PlayUIFX("ui_back_02");
	}

	private static bool IsOn(Row r) => r.Toggle == null ? r.Mod.Enabled : r.Mod.Context.Settings.On(r.Toggle.Key);

	private static void Toggle(int i)
	{
		if (i < 0 || i >= rows.Count)
		{
			return;
		}
		Row r = rows[i];
		if (r.Toggle == null)
		{
			Loader.SetEnabled(r.Mod.Info.Id, !r.Mod.Enabled);
		}
		else
		{
			r.Mod.Context.Settings.Toggle(r.Toggle.Key);
		}
		dmAudio.PlayUIFX(IsOn(r) ? "ui_increase_02" : "ui_decrease_02");
	}

	// ---------- drawing ----------

	// Where the "Mod Settings" button goes right now, if anywhere.
	private static bool ButtonRect(dmGame g, out Rectangle r)
	{
		wg2DRenderer tr = g.theRenderer;
		r = Rectangle.Empty;
		if (g.bOptionsMode && !g.bOptionsMode_Hotbars && !g.bKeybindsUIMode)
		{
			// Just under the vanilla options parchment (same size formula as Dialog_Options_2021).
			int count = (g.bShowMainMenu ? 5 : 6) + tr.optionsList.Count;
			int heightUnits = (count * 40 + 32) / 2 / 32 + 4;
			r = new Rectangle((int)(tr.fWindowXSize * 0.5f) - 128, 32 + heightUnits * 32 + 8, 256, 32);
			return true;
		}
		if (g.bShowMainMenu && g.mainMenu != null && g.mainMenu.eMainMenuState == dmMainMenuInterface.MainMenuState.MMS_MainTitle && !g.bDialogBoxMode)
		{
			r = new Rectangle(16, (int)tr.fWindowYSize - 56, 256, 32);
			return true;
		}
		return false;
	}

	public static void Draw(dmGame g)
	{
		try
		{
			if (IsOpen)
			{
				DrawPanel(g);
			}
			else if (ButtonRect(g, out Rectangle r))
			{
				wg2DRenderer tr = g.theRenderer;
				tr.BeginSpriteBatch();
				tr.bForceDropShadows = true;
				tr.DrawParchmentBox(r.X, r.Y, r.Width / 32, 1);
				ui_mouseFrame f = Ui.Area("modkit_settings_button", r, Open, 30);
				Ui.Text("Mod Settings", r.X + 74, r.Y + 6, Ui.Hovered(f) ? ShepColor.Yellow : Color.White);
				tr.EndSpriteBatch();
			}
		}
		catch (Exception e)
		{
			IsOpen = false;
			KitLog.Write("Mod Settings draw failed: " + e);
		}
	}

	private const int PanelUnitsW = 24, RowH = 34;
	private static int visibleRows = 12;

	private static string StateNote(LoadedMod m)
	{
		bool loadedNow = m.State != "disabled";
		if (m.Enabled != loadedNow)
		{
			return "^3(restart the game to apply)^7";
		}
		switch (m.State)
		{
			case "failed":
				return "^1(failed to load)^7";
			case "no code":
				return "(data only)";
			case "loaded":
				int failed = m.Context.Patches.Count(p => !p.Ok);
				return failed > 0 ? "^1(" + failed + " part" + (failed > 1 ? "s" : "") + " failed)^7" : "";
			default:
				return "";
		}
	}

	private static string Help(Row r)
	{
		if (r.Toggle != null)
		{
			return r.Toggle.Description;
		}
		LoadedMod m = r.Mod;
		string s = (string.IsNullOrEmpty(m.Info.Description) ? "" : m.Info.Description + " ")
			+ (string.IsNullOrEmpty(m.Info.Author) ? "" : "By " + m.Info.Author + ". ")
			+ "Switching a whole mod on/off applies at the next launch.";
		if (m.Error != null)
		{
			string err = m.Error.Split('\n')[0].Trim();
			s += " ^1" + (err.Length > 160 ? err.Substring(0, 157) + "..." : err) + "^7 See ModKit\\modkit.log.";
		}
		else if (m.Context?.Patches.Any(p => !p.Ok) == true)
		{
			s += " ^1Not working (game update?): " + string.Join(", ", m.Context.Patches.Where(p => !p.Ok).Select(p => p.Name)) + "^7";
		}
		return s;
	}

	private static void DrawPanel(dmGame g)
	{
		wg2DRenderer tr = g.theRenderer;
		int w = PanelUnitsW * 32;
		visibleRows = Math.Max(4, ((int)tr.fWindowYSize - 64 - 64 - 150) / RowH);
		int shown = Math.Min(visibleRows, rows.Count);
		int heightUnits = (64 + shown * RowH + 120) / 32;
		int x = (int)(tr.fWindowXSize * 0.5f) - w / 2;
		int y = Math.Max(8, (int)(tr.fWindowYSize * 0.5f) - (heightUnits * 32 + 32) / 2);

		tr.BeginSpriteBatch();
		tr.bForceDropShadows = true;
		Ui.DimAndBlock("modkit_settings", 30);
		tr.DrawParchmentBox(x, y, PanelUnitsW, heightUnits);
		Ui.Text("Mod Settings", x + 24, y + 12, ShepColor.Yellow, UiFont.Large);
		Ui.Text("Click (or Enter) to switch on/off. Saved immediately.", x + 250, y + 22, Color.LightGray);
		ui_mouseFrame close = Ui.Area("modkit_settings_close", new Rectangle(x + w - 44, y + 8, 36, 32), Close, 31);
		Ui.Text("X", x + w - 34, y + 10, Ui.Hovered(close) ? ShepColor.Yellow : Color.White, UiFont.Medium);

		int rowY = y + 64;
		if (rows.Count == 0)
		{
			Ui.Text("No mods found. Put mods in the game's Mods folder.", x + 28, rowY + 6, Color.LightGray, UiFont.Medium);
		}
		scroll = Math.Max(0, Math.Min(scroll, rows.Count - shown));
		for (int vi = 0; vi < shown; vi++)
		{
			int i = scroll + vi;
			Row r = rows[i];
			int ry = rowY + vi * RowH;
			int idx = i;
			ui_mouseFrame rf = Ui.Area("modkit_settings_row_" + vi, new Rectangle(x + 16, ry, w - 32, RowH - 4), () => Toggle(idx), 31);
			if (Ui.Hovered(rf))
			{
				selected = i;
			}
			bool on = IsOn(r);
			Color label = i == selected ? ShepColor.Yellow : on ? Color.White : Color.Gray;
			if (r.Toggle == null)
			{
				Ui.Text(on ? "[ON]" : "[off]", x + 28, ry + 4, on ? ShepColor.Cyan : Color.DarkGray, UiFont.Medium);
				Ui.Text(r.Mod.Info.Name + "  ^7v" + r.Mod.Info.Version + "  " + StateNote(r.Mod), x + 100, ry + 2, label, UiFont.Medium);
			}
			else
			{
				Ui.Text(on ? "[ON]" : "[off]", x + 64, ry + 6, on ? ShepColor.Cyan : Color.DarkGray);
				Ui.Text(r.Toggle.Label, x + 124, ry + 6, label);
			}
		}
		if (scroll > 0)
		{
			Ui.Button("modkit_settings_up", x + w - 140, y + 36, "more above", () => scroll = Math.Max(0, scroll - shown), stackLevel: 31);
		}
		if (scroll + shown < rows.Count)
		{
			Ui.Button("modkit_settings_down", x + w - 140, rowY + shown * RowH - 4, "more below", () => scroll = Math.Min(rows.Count - shown, scroll + shown), stackLevel: 31);
		}

		int helpY = rowY + shown * RowH + 20;
		tr.DrawLine(x + 16, helpY - 6, x + w - 16, helpY - 6, 1, Color.Gray);
		if (selected < rows.Count)
		{
			Ui.WrappedText("^3?^7 " + Help(rows[selected]), x + 24, helpY + 4, w - 48);
		}
		tr.EndSpriteBatch();
	}
}
