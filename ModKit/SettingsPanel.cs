using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMans;
using Microsoft.Xna.Framework;

namespace DungeonmansModKit;

// "Mod Settings": one card per mod (whole-mod switch, applies at next launch), then its switches grouped under
// headings (apply immediately). Descriptions are tooltips. Opened from the title screen and under the Options window.
internal static class SettingsPanel
{
	public static bool IsOpen;
	private static int selected;
	private static int scroll; // in pixels

	private enum Kind
	{
		Mod,
		Group,
		Toggle,
	}

	private sealed class Row
	{
		public Kind Kind;
		public LoadedMod Mod;
		public ModToggle Toggle;
		public string Group;
		public int Height => Kind == Kind.Mod ? 72 : 32;
		public bool Selectable => Kind != Kind.Group;
	}

	private static List<Row> rows = new List<Row>();

	private static void Rebuild()
	{
		rows = new List<Row>();
		foreach (LoadedMod m in Loader.Mods)
		{
			rows.Add(new Row { Kind = Kind.Mod, Mod = m });
			if (m.Context == null || m.State != "loaded")
			{
				continue;
			}
			string group = null;
			foreach (ModToggle t in m.Context.Settings.Toggles)
			{
				if (t.Group != null && t.Group != group)
				{
					group = t.Group;
					rows.Add(new Row { Kind = Kind.Group, Mod = m, Group = group });
				}
				rows.Add(new Row { Kind = Kind.Toggle, Mod = m, Toggle = t });
			}
		}
		selected = rows.FindIndex(r => r.Selectable);
	}

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
			Step(-1);
		}
		else if (a.Contains(ActionButton.Down) || a.Contains(ActionButton.ArrowKey_Down))
		{
			Step(1);
		}
		else if (a.Contains(ActionButton.MouseWheel_Up))
		{
			scroll -= 96;
		}
		else if (a.Contains(ActionButton.MouseWheel_Down))
		{
			scroll += 96;
		}
		else if (a.Contains(ActionButton.Enter) || a.Contains(ActionButton.Select)
			|| a.Contains(ActionButton.Left) || a.Contains(ActionButton.Right))
		{
			Toggle(selected);
		}
		a.Clear();
	}

	private static void Step(int dir)
	{
		int i = selected;
		do
		{
			i += dir;
		}
		while (i >= 0 && i < rows.Count && !rows[i].Selectable);
		if (i >= 0 && i < rows.Count)
		{
			selected = i;
			keepVisible = true;
		}
	}

	private static bool keepVisible;

	public static void Open()
	{
		Rebuild();
		IsOpen = true;
		scroll = 0;
		dmAudio.PlayUIFX("open_dialog");
	}

	public static void Close()
	{
		IsOpen = false;
		dmAudio.PlayUIFX("ui_back_02");
	}

	private static bool IsOn(Row r) => r.Kind == Kind.Mod ? r.Mod.Enabled : r.Mod.Context.Settings.On(r.Toggle.Key);

	private static void Toggle(int i)
	{
		if (i < 0 || i >= rows.Count || !rows[i].Selectable)
		{
			return;
		}
		Row r = rows[i];
		if (r.Kind == Kind.Mod)
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
			r = new Rectangle((int)(tr.fWindowXSize * 0.5f) - 120, 32 + heightUnits * 32 + 8, 240, 40);
			return true;
		}
		if (g.bShowMainMenu && g.mainMenu != null && g.mainMenu.eMainMenuState == dmMainMenuInterface.MainMenuState.MMS_MainTitle && !g.bDialogBoxMode)
		{
			r = new Rectangle(16, (int)tr.fWindowYSize - 60, 240, 40);
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
				Ui.BoxButton("modkit_settings_button", r, "Mod Settings", Open, font: UiFont.Medium, stackLevel: 30);
				tr.EndSpriteBatch();
			}
		}
		catch (Exception e)
		{
			IsOpen = false;
			KitLog.Write("Mod Settings draw failed: " + e);
		}
	}

	private static (string text, Color color) State(LoadedMod m)
	{
		bool loadedNow = m.State != "disabled";
		if (m.Enabled != loadedNow)
		{
			return ("restart to apply", Palette.Warn);
		}
		switch (m.State)
		{
			case "failed":
				return ("failed to load", Palette.Bad);
			case "no code":
				return ("data only", Palette.TextDim);
			case "disabled":
				return ("off", Palette.TextMuted);
			default:
				int failed = m.Context.Patches.Count(p => !p.Ok);
				return failed > 0 ? (failed + " part" + (failed > 1 ? "s" : "") + " broken", Palette.Bad) : ("running", Palette.Good);
		}
	}

	private static string ModHelp(LoadedMod m)
	{
		string s = (m.Info.Description ?? "") + (string.IsNullOrEmpty(m.Info.Author) ? "" : "\n^7By " + m.Info.Author);
		if (m.Error != null)
		{
			string err = m.Error.Split('\n')[0].Trim();
			s += "\n^1" + (err.Length > 90 ? err.Substring(0, 87) + "..." : err) + "^7";
		}
		else if (m.Context?.Patches.Any(p => !p.Ok) == true)
		{
			s += "\n^1Broken: " + string.Join(", ", m.Context.Patches.Where(p => !p.Ok).Select(p => p.Name)) + "^7";
		}
		return s + "\n^7Whole-mod switch: applies at next launch.";
	}

	private const int WidthUnits = 22;

	private static void DrawPanel(dmGame g)
	{
		wg2DRenderer tr = g.theRenderer;
		int total = rows.Sum(r => r.Height);
		int maxListH = (int)tr.fWindowYSize - 260;
		int listH = Math.Min(total, maxListH);
		int heightUnits = (listH + 66 + 20 + 36 + 31) / 32;

		tr.BeginSpriteBatch();
		tr.bForceDropShadows = true;
		Rectangle c = Ui.ModalWindow("modkit_settings", WidthUnits, heightUnits, "Mod Settings", Close, "changes save instantly");
		var list = new Rectangle(c.X, c.Y, c.Width, c.Height - 36);

		if (rows.Count == 0)
		{
			Ui.TextCentered("No mods in the Mods folder yet.", list, Palette.TextDim, UiFont.Medium);
		}
		scroll = Math.Max(0, Math.Min(scroll, Math.Max(0, total - list.Height)));
		if (keepVisible && selected >= 0)
		{
			keepVisible = false;
			int top = rows.Take(selected).Sum(r => r.Height);
			if (top < scroll)
			{
				scroll = top;
			}
			else if (top + rows[selected].Height > scroll + list.Height)
			{
				scroll = top + rows[selected].Height - list.Height;
			}
		}

		int y = list.Y - scroll;
		for (int i = 0; i < rows.Count; i++)
		{
			Row r = rows[i];
			int h = r.Height;
			if (y + h > list.Y && y < list.Bottom - 2 && y >= list.Y - 2)
			{
				DrawRow(r, i, new Rectangle(list.X, y, list.Width - 14, h));
			}
			y += h;
		}

		// Scrollbar
		if (total > list.Height)
		{
			var track = new Rectangle(list.Right - 8, list.Y, 6, list.Height);
			Ui.Fill(track, Palette.BarBack);
			int thumbH = Math.Max(24, list.Height * list.Height / total);
			int thumbY = track.Y + (int)((track.Height - thumbH) * (scroll / (float)Math.Max(1, total - list.Height)));
			Ui.Fill(new Rectangle(track.X, thumbY, track.Width, thumbH), Palette.Border);
		}

		// Footer: keyboard hint
		Ui.Separator(c.X, c.Bottom - 30, c.Width);
		Ui.Text("Hover for details   ^3Up/Down^7 select   ^3Enter^7 switch   ^3Esc^7 close", c.X + 4, c.Bottom - 24, Palette.TextDim);
		tr.EndSpriteBatch();
	}

	private static void DrawRow(Row r, int i, Rectangle rect)
	{
		int idx = i;
		bool sel = i == selected;
		switch (r.Kind)
		{
		case Kind.Mod:
		{
			ui_mouseFrame f = Ui.Area("modkit_settings_row_" + i, rect, () => Toggle(idx), 31);
			bool hot = Ui.Hovered(f);
			if (hot)
			{
				selected = i;
				Ui.Tooltip(ModHelp(r.Mod));
			}
			Rectangle box = Ui.Box(new Rectangle(rect.X, rect.Y, rect.Width, 64), sel || hot);
			Ui.Text(r.Mod.Info.Name, box.X + 16, box.Y + 8, Palette.Accent, UiFont.Large);
			(string text, Color color) = State(r.Mod);
			Ui.Text("v" + r.Mod.Info.Version + "    " + text, box.X + 18, box.Y + 40, color);
			Ui.Checkbox("modkit_settings_sw_" + i, box.Right - 84, box.Y + 16, r.Mod.Enabled, () => Toggle(idx), stackLevel: 32);
			break;
		}
		case Kind.Group:
			Ui.Text(r.Group, rect.X + 6, rect.Y + 6, Palette.Accent, UiFont.Medium);
			int tw = (int)Ui.Measure(r.Group, UiFont.Medium).X;
			Ui.Separator(rect.X + tw + 18, rect.Y + 19, rect.Width - tw - 24);
			break;
		default:
		{
			ui_mouseFrame f = Ui.Area("modkit_settings_row_" + i, rect, () => Toggle(idx), 31);
			bool hot = Ui.Hovered(f);
			bool on = IsOn(r);
			if (hot)
			{
				selected = i;
				if (!string.IsNullOrEmpty(r.Toggle.Description))
				{
					Ui.Tooltip(r.Toggle.Description);
				}
			}
			if (sel || hot)
			{
				Ui.Fill(new Rectangle(rect.X + 4, rect.Y + 2, rect.Width - 8, rect.Height - 4), new Color(255, 230, 170, 40));
			}
			Ui.Text(r.Toggle.Label, rect.X + 24, rect.Y + 4, on ? Palette.Text : Palette.TextMuted, UiFont.Medium);
			Ui.Checkbox("modkit_settings_sw_" + i, rect.Right - 84, rect.Y, on, () => Toggle(idx), stackLevel: 32);
			break;
		}
		}
	}
}
