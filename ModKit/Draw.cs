using System;
using System.Collections.Generic;
using System.Text;
using DungeonMans;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DungeonmansModKit;

/// <summary>Text colors that read well on the game's boxes. Use them so mods look consistent.</summary>
public static class Palette
{
	public static readonly Color Text = new Color(236, 228, 210);
	public static readonly Color TextDim = new Color(186, 172, 148);
	public static readonly Color TextMuted = new Color(128, 116, 100);
	public static readonly Color Accent = new Color(250, 206, 92);
	public static readonly Color Good = new Color(120, 210, 110);
	public static readonly Color Warn = new Color(236, 170, 70);
	public static readonly Color Bad = new Color(232, 92, 78);
	public static readonly Color Info = new Color(110, 196, 240);
	public static readonly Color Border = new Color(112, 88, 60);
	public static readonly Color BorderBright = new Color(214, 176, 92);

	// Kept for flat drawing (small details like map cells).
	public static readonly Color Panel = new Color(24, 18, 14, 215);
	public static readonly Color PanelRaised = new Color(44, 34, 26, 235);
	public static readonly Color PanelHover = new Color(70, 54, 36, 240);
	public static readonly Color PanelSelected = new Color(92, 70, 30, 245);
	public static readonly Color BarBack = new Color(12, 9, 7, 230);

	/// <summary>Green at full, yellow at half, red when low.</summary>
	public static Color Health(float fraction) =>
		fraction > 0.5f ? Color.Lerp(Warn, Good, (fraction - 0.5f) * 2f) : Color.Lerp(Bad, Warn, Math.Max(0f, fraction) * 2f);
}

/// <summary>The game's own bar graphics (from the hero's vitals frame).</summary>
public enum BarStyle
{
	Health,   // red
	Stamina,  // yellow
	Mana,     // blue
	Ire,      // orange
	Xp,       // green ribbon
	Fill,     // blue fill bar (progress)
}

public static partial class Ui
{
	/// <summary>Opens the Mod Settings window (e.g. from your own "settings" button).</summary>
	public static void OpenModSettings() => SettingsPanel.Open();

	/// <summary>Closes the Mod Settings window if it's open.</summary>
	public static void CloseModSettings()
	{
		if (SettingsPanel.IsOpen)
		{
			SettingsPanel.Close();
		}
	}

	// ---------- mouse ----------

	/// <summary>Mouse position in UI pixels.</summary>
	public static Point Mouse => Dm.Game.theInput.vMousePosition.ToPoint();
	public static bool MouseDown { get; private set; }
	/// <summary>The left button went down this frame.</summary>
	public static bool MousePressed { get; private set; }
	/// <summary>The left button went up this frame.</summary>
	public static bool MouseReleased { get; private set; }

	internal static void BeginFrame()
	{
		bool down = Microsoft.Xna.Framework.Input.Mouse.GetState().LeftButton == ButtonState.Pressed;
		MousePressed = down && !MouseDown;
		MouseReleased = !down && MouseDown;
		MouseDown = down;
		tooltip = null;
	}

	// ---------- low-level ----------

	private static Texture2D pixel;
	private static readonly AccessTools.FieldRef<wg2DRenderer, SpriteBatch> BatchRef = AccessTools.FieldRefAccess<wg2DRenderer, SpriteBatch>("spriteBatch");

	private static SpriteBatch Batch => BatchRef(Dm.Renderer);

	private static Texture2D Pixel
	{
		get
		{
			if (pixel == null || pixel.IsDisposed)
			{
				pixel = new Texture2D(Dm.Game.GraphicsDevice, 1, 1);
				pixel.SetData(new[] { Color.White });
			}
			return pixel;
		}
	}

	/// <summary>A solid rectangle. Colors are normal (not premultiplied): new Color(r, g, b, alpha) works as expected.</summary>
	public static void Fill(Rectangle r, Color c)
	{
		if (r.Width > 0 && r.Height > 0 && c.A > 0)
		{
			Batch.Draw(Pixel, r, c.A == 255 ? c : Color.FromNonPremultiplied(c.R, c.G, c.B, c.A));
		}
	}

	public static void Outline(Rectangle r, Color c, int thickness = 1)
	{
		Fill(new Rectangle(r.X, r.Y, r.Width, thickness), c);
		Fill(new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), c);
		Fill(new Rectangle(r.X, r.Y, thickness, r.Height), c);
		Fill(new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), c);
	}

	/// <summary>A flat box with a thin border (for small details; prefer <see cref="Box"/> for panels).</summary>
	public static void Panel(Rectangle r, Color? fill = null, Color? border = null)
	{
		Fill(r, fill ?? Palette.Panel);
		Outline(r, border ?? Palette.Border);
	}

	/// <summary>A thin divider line.</summary>
	public static void Separator(int x, int y, int width) => Fill(new Rectangle(x, y, width, 1), new Color((int)Palette.Border.R, Palette.Border.G, Palette.Border.B, 170));

	// ---------- game sprites ----------

	private static readonly Dictionary<string, dmSprite> sprites = new Dictionary<string, dmSprite>();

	/// <summary>One of the game's sprites by name (cached). Names are in Content\SpriteData\*.txt.</summary>
	public static dmSprite GameSprite(string name)
	{
		if (!sprites.TryGetValue(name, out dmSprite sp))
		{
			try
			{
				sp = Dm.Renderer.GetSpriteByName(name);
			}
			catch
			{
				sp = default;
			}
			sprites[name] = sp;
		}
		return sp;
	}

	/// <summary>Draws a sprite scaled to fit inside a box, centered, keeping its aspect ratio.</summary>
	public static void Sprite(dmSprite sprite, Rectangle box, Color? tint = null)
	{
		if (sprite.Width <= 0 || sprite.Height <= 0)
		{
			return;
		}
		float scale = Math.Min(box.Width / (float)sprite.Width, box.Height / (float)sprite.Height);
		int w = (int)(sprite.Width * scale), h = (int)(sprite.Height * scale);
		Dm.Renderer.DrawSpriteToScreenCoordinates(sprite, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h, false, tint ?? Color.White);
	}

	/// <summary>Draws a game sprite by name, fitted in a box (e.g. "ui_favorite_heart", "ui_lock", "ui_levelup").</summary>
	public static void Icon(string spriteName, Rectangle box, Color? tint = null) => Sprite(GameSprite(spriteName), box, tint);

	/// <summary>Draws a game sprite stretched to fill a rectangle (or a horizontal part of it: 0..1).</summary>
	public static void Stretch(string spriteName, Rectangle r, float part = 1f, Color? tint = null)
	{
		dmSprite sp = GameSprite(spriteName);
		if (sp.Width <= 0 || part <= 0f)
		{
			return;
		}
		part = Math.Min(1f, part);
		var src = new Rectangle(sp.xLoc, sp.yLoc, Math.Max(1, (int)(sp.Width * part)), sp.Height);
		var dst = new Rectangle(r.X, r.Y, Math.Max(1, (int)(r.Width * part)), r.Height);
		Batch.Draw(Dm.Renderer.texturesByID[sp.iTextureID].Texture, dst, src, tint ?? Color.White);
	}

	private static readonly Dictionary<string, dmSpecialPower> powers = new Dictionary<string, dmSpecialPower>();

	/// <summary>The game's power definition for an archetype (cached), or null.</summary>
	public static dmSpecialPower Power(string archetype)
	{
		if (string.IsNullOrEmpty(archetype))
		{
			return null;
		}
		if (!powers.TryGetValue(archetype, out dmSpecialPower p))
		{
			try
			{
				p = Dm.Game.ReadUniqueGenerationDataArchetype<dmSpecialPower>(archetype);
			}
			catch
			{
				p = null;
			}
			powers[archetype] = p;
		}
		return p;
	}

	/// <summary>Draws a power's icon (the same icons as the hotbar). Returns false if the power is unknown.</summary>
	public static bool PowerIcon(string archetype, Rectangle r, bool active = true)
	{
		dmSpecialPower p = Power(archetype);
		if (p == null)
		{
			return false;
		}
		Dm.Renderer.DrawPowerIconToScreen(r, p, active, false);
		return true;
	}

	// ---------- game boxes, buttons, bars ----------

	/// <summary>
	/// The game's dark text box (the one used for text fields): the base for cards, rows, tabs and sections.
	/// The game draws boxes in 32-pixel tiles, so the size is rounded to 32; lay out on a 32-pixel grid.
	/// Returns the rectangle actually covered. highlighted = gold border (selected).
	/// </summary>
	public static Rectangle Box(Rectangle r, bool highlighted = false)
	{
		int tw = Math.Max(2, r.Width / 32), th = Math.Max(1, r.Height / 32);
		Dm.Renderer.DrawParchmentTextBox(r.X, r.Y, tw, th, highlighted);
		return new Rectangle(r.X, r.Y, tw * 32, th * 32);
	}

	/// <summary>The game's progress bar graphics, stretched to a rectangle, with an optional centered label.</summary>
	public static void GameBar(BarStyle style, Rectangle r, float fraction, string label = null, UiFont font = UiFont.Small)
	{
		fraction = MathHelper.Clamp(fraction, 0f, 1f);
		Stretch("ook_ui_bag_statbar_empty", r);
		string fill = style switch
		{
			BarStyle.Health => "ook_ui_bag_healthbar",
			BarStyle.Stamina => "ook_ui_bag_staminabar",
			BarStyle.Mana => "ook_ui_bag_manabar",
			BarStyle.Ire => "ook_ui_bag_irebar",
			_ => "ook_ui_bag_manabar",
		};
		if (style == BarStyle.Xp)
		{
			Stretch("ook_ui_bag_xp_ribbon", new Rectangle(r.X + 3, r.Y + r.Height / 4, r.Width - 6, r.Height / 2), fraction);
		}
		else
		{
			Stretch(fill, r, fraction);
		}
		if (label != null)
		{
			bool shadows = Dm.Renderer.bForceDropShadows;
			Dm.Renderer.bForceDropShadows = true;
			TextCentered(label, r, Color.White, font);
			Dm.Renderer.bForceDropShadows = shadows;
		}
	}

	/// <summary>Backwards-compatible bar: picks the closest game bar for the color.</summary>
	public static void Bar(Rectangle r, float fraction, Color fill, string label = null, UiFont font = UiFont.Small)
	{
		BarStyle s = fill.R > 200 && fill.G < 120 ? BarStyle.Health : fill.R > 200 && fill.G > 150 && fill.B < 120 ? BarStyle.Stamina
			: fill.G > fill.R && fill.G > fill.B ? BarStyle.Xp : fill.R > 200 ? BarStyle.Ire : BarStyle.Mana;
		GameBar(s, r, fraction, label, font);
	}

	/// <summary>
	/// The game's slider (brown rail with end caps and a red gem marker) showing a value in [min, max],
	/// with a tick at the middle. Use for two-sided stats.
	/// </summary>
	public static void Slider(Rectangle r, int value, int min, int max)
	{
		int capW = r.Height;
		var rail = new Rectangle(r.X, r.Y + (r.Height - 20) / 2, r.Width, 20);
		Stretch("ui_slider_left_cap", new Rectangle(rail.X, rail.Y, capW, rail.Height));
		Stretch("ui_slider_base", new Rectangle(rail.X + capW, rail.Y, rail.Width - 2 * capW, rail.Height));
		Stretch("ui_slider_right_cap", new Rectangle(rail.Right - capW, rail.Y, capW, rail.Height));
		int inner = rail.Width - 2 * capW;
		int mid = rail.X + capW + inner / 2;
		Fill(new Rectangle(mid, rail.Y + 3, 2, rail.Height - 6), new Color(60, 40, 24, 220));
		float t = (value - min) / (float)Math.Max(1, max - min);
		int mx = rail.X + capW + (int)(inner * t);
		int mh = r.Height + 8;
		int mw = Math.Max(8, mh * 19 / 42);
		Stretch("ui_slider_marker", new Rectangle(mx - mw / 2, r.Y + r.Height / 2 - mh / 2, mw, mh));
	}

	/// <summary>
	/// An On/Off switch in the style of the game's Options menu: a 64x32 game box reading "On" (gold border) or "Off".
	/// Returns true while hovered.
	/// </summary>
	public static bool Checkbox(string id, int x, int y, bool on, Action onToggle, string tooltipText = null, int stackLevel = WindowLevel + 1)
	{
		var r = new Rectangle(x, y, 64, 32);
		ui_mouseFrame f = Area(id, r, () => onToggle?.Invoke(), stackLevel);
		bool hot = Hovered(f);
		Box(r, on);
		TextCentered(on ? "On" : "Off", r, on ? Palette.Accent : hot ? Palette.Text : Palette.TextMuted, UiFont.Medium);
		if (hot && tooltipText != null)
		{
			Tooltip(tooltipText);
		}
		return hot;
	}

	/// <summary>Old name for <see cref="Checkbox"/> (now draws the game's checkbox).</summary>
	public static bool Switch(string id, Rectangle r, bool on, Action onToggle, string tooltipText = null, int stackLevel = WindowLevel + 1) =>
		Checkbox(id, r.X, r.Y + (r.Height - 24) / 2, on, onToggle, tooltipText, stackLevel);

	// ---------- text ----------

	/// <summary>Removes the game's ^x color codes.</summary>
	public static string StripCodes(string s)
	{
		if (string.IsNullOrEmpty(s) || s.IndexOf('^') < 0)
		{
			return s ?? "";
		}
		var sb = new StringBuilder(s.Length);
		for (int i = 0; i < s.Length; i++)
		{
			if (s[i] == '^' && i + 1 < s.Length)
			{
				i++;
				continue;
			}
			sb.Append(s[i]);
		}
		return sb.ToString();
	}

	public static Vector2 Measure(string text, UiFont font = UiFont.Small)
	{
		SetFont(font);
		return Dm.Renderer.GetActiveFont().MeasureString(StripCodes(text));
	}

	public static int LineHeight(UiFont font) => font == UiFont.Large || font == UiFont.Title ? 34 : font == UiFont.Medium ? 26 : 20;

	public static void TextCentered(string text, Rectangle r, Color? color = null, UiFont font = UiFont.Small)
	{
		Vector2 size = Measure(text, font);
		Text(text, r.X + (int)((r.Width - size.X) / 2), r.Y + (int)((r.Height - size.Y) / 2) + 1, color, font);
	}

	public static void TextRight(string text, int right, int y, Color? color = null, UiFont font = UiFont.Small) =>
		Text(text, right - (int)Measure(text, font).X, y, color, font);

	/// <summary>Text cut with "..." to fit a width.</summary>
	public static string Fit(string text, int width, UiFont font = UiFont.Small)
	{
		if (Measure(text, font).X <= width)
		{
			return text;
		}
		string plain = StripCodes(text);
		while (plain.Length > 1 && Measure(plain + "...", font).X > width)
		{
			plain = plain.Substring(0, plain.Length - 1);
		}
		return plain + "...";
	}

	// ---------- widgets ----------

	/// <summary>
	/// A button drawn with the game's steel button graphics (lit when hovered or selected). Width is rounded to
	/// 32-pixel tiles. Returns true while hovered.
	/// </summary>
	public static bool BoxButton(string id, Rectangle r, string label, Action onClick, bool enabled = true, bool selected = false,
		UiFont font = UiFont.Small, string tooltipText = null, Color? accent = null, int stackLevel = WindowLevel + 1)
	{
		int tw = Math.Max(2, (r.Width + 16) / 32);
		int th = Math.Max(1, (r.Height + 16) / 32);
		var box = new Rectangle(r.X, r.Y, tw * 32, th * 32);
		ui_mouseFrame f = Area(id, box, enabled ? () => { dmAudio.PlayUIFX("ui_click_01"); onClick?.Invoke(); } : null, stackLevel);
		bool hot = Hovered(f);
		if (enabled && (hot || selected))
		{
			Dm.Renderer.DrawHighlightButton(box.X, box.Y, tw, th);
		}
		else
		{
			Dm.Renderer.DrawDarkButton(box.X, box.Y, tw, th);
		}
		if (!enabled)
		{
			Fill(box, new Color(20, 16, 12, 140));
		}
		Color text = !enabled ? Palette.TextMuted : selected ? Palette.Accent : hot ? Color.White : (accent ?? Palette.Text);
		bool shadows = Dm.Renderer.bForceDropShadows;
		Dm.Renderer.bForceDropShadows = true;
		TextCentered(label, box, text, font);
		Dm.Renderer.bForceDropShadows = shadows;
		if (hot && tooltipText != null)
		{
			Tooltip(tooltipText);
		}
		return hot;
	}

	/// <summary>Width a BoxButton needs for a label (a multiple of 32).</summary>
	public static int ButtonWidth(string label, UiFont font = UiFont.Small) => ((int)Measure(label, font).X + 24 + 31) / 32 * 32;

	/// <summary>A small colored tag (status, count). Draws at x and returns the x after it.</summary>
	public static int Chip(int x, int y, string text, Color color, UiFont font = UiFont.Small)
	{
		Text(text, x, y + 1, color, font);
		return x + (int)Measure(text, font).X + 14;
	}

	/// <summary>
	/// Row of tabs (game boxes; the current one has a gold border). Returns the row height (32).
	/// Disabled tabs (enabled[i] == false) are grayed out.
	/// </summary>
	public static int Tabs(string id, int x, int y, string[] labels, int current, Action<int> onPick, bool[] enabled = null, UiFont font = UiFont.Medium)
	{
		for (int i = 0; i < labels.Length; i++)
		{
			int idx = i;
			bool on = enabled == null || enabled[i];
			int w = Math.Max(96, ((int)Measure(labels[i], font).X + 32 + 31) / 32 * 32);
			var r = new Rectangle(x, y, w, 32);
			ui_mouseFrame f = Area(id + "_tab" + i, r, on ? () => { dmAudio.PlayUIFX("ui_click_01"); onPick(idx); } : (Action)null, WindowLevel + 1);
			bool hot = Hovered(f);
			Box(r, i == current);
			TextCentered(labels[i], r, !on ? Palette.TextMuted : i == current ? Palette.Accent : hot ? Color.White : Palette.TextDim, font);
			x += w + 4;
		}
		return 32;
	}

	// ---------- tooltips ----------

	private static string tooltip;

	/// <summary>Shows a tooltip next to the mouse this frame (call while something is hovered). Supports color codes and \n.</summary>
	public static void Tooltip(string text) => tooltip = text;

	internal static void FlushTooltip()
	{
		if (string.IsNullOrEmpty(tooltip) || Dm.Renderer == null)
		{
			return;
		}
		wg2DRenderer tr = Dm.Renderer;
		string[] lines = tooltip.Split('\n');
		int w = 0;
		foreach (string l in lines)
		{
			w = Math.Max(w, (int)Measure(l).X);
		}
		int tw = (w + 28 + 31) / 32, th = (lines.Length * LineHeight(UiFont.Small) + 14 + 31) / 32;
		Point m = Mouse;
		var r = new Rectangle(m.X + 20, m.Y + 20, tw * 32, th * 32);
		if (r.Right > tr.fWindowXSize - 4)
		{
			r.X = m.X - r.Width - 8;
		}
		if (r.Bottom > tr.fWindowYSize - 4)
		{
			r.Y = m.Y - r.Height - 8;
		}
		tr.BeginSpriteBatch();
		tr.DrawParchmentTextBox(r.X, r.Y, tw, th, true);
		int ty = r.Y + (r.Height - lines.Length * LineHeight(UiFont.Small)) / 2;
		for (int i = 0; i < lines.Length; i++)
		{
			Text(lines[i], r.X + 14, ty + i * LineHeight(UiFont.Small), Palette.Text);
		}
		tr.EndSpriteBatch();
		tooltip = null;
	}

	// ---------- toasts ----------

	private sealed class ToastItem
	{
		public string Text;
		public Color Accent;
		public DateTime Until;
	}

	private static readonly List<ToastItem> toasts = new List<ToastItem>();

	/// <summary>
	/// A short message shown at the top center of the screen for a few seconds (newest at the bottom, max 4).
	/// Use for feedback like "Speed x2" or "Pantry full" that would get lost in the message log.
	/// </summary>
	public static void Toast(string text, Color? accent = null, float seconds = 2.5f)
	{
		toasts.Add(new ToastItem { Text = text, Accent = accent ?? Palette.Accent, Until = DateTime.Now.AddSeconds(seconds) });
		while (toasts.Count > 4)
		{
			toasts.RemoveAt(0);
		}
	}

	internal static void FlushToasts()
	{
		toasts.RemoveAll(t => t.Until < DateTime.Now);
		if (toasts.Count == 0 || Dm.Renderer == null)
		{
			return;
		}
		wg2DRenderer tr = Dm.Renderer;
		tr.BeginSpriteBatch();
		int y = 56;
		foreach (ToastItem t in toasts)
		{
			Vector2 size = Measure(t.Text, UiFont.Medium);
			int tw = ((int)size.X + 48 + 31) / 32;
			int x = ((int)tr.fWindowXSize - tw * 32) / 2;
			tr.DrawParchmentTextBox(x, y, tw, 2, false);
			Fill(new Rectangle(x + 6, y + 8, 3, 48), t.Accent);
			bool shadows = tr.bForceDropShadows;
			tr.bForceDropShadows = true;
			TextCentered(t.Text, new Rectangle(x, y, tw * 32, 64), Palette.Text, UiFont.Medium);
			tr.bForceDropShadows = shadows;
			y += 70;
		}
		tr.EndSpriteBatch();
	}

	// ---------- windows ----------

	/// <summary>
	/// The standard modal window: dims the screen, blocks clicks behind it, draws a centered parchment window
	/// (widthUnits x heightUnits tiles of 32 px) with a title, an optional subtitle and a close button.
	/// Returns the content rectangle below the title. Call inside BeginSpriteBatch/EndSpriteBatch.
	/// </summary>
	public static Rectangle ModalWindow(string id, int widthUnits, int heightUnits, string title, Action onClose, string subtitle = null)
	{
		wg2DRenderer tr = Dm.Renderer;
		DimAndBlock(id);
		int w = widthUnits * 32, h = heightUnits * 32;
		int x = (int)(tr.fWindowXSize - w) / 2;
		int y = Math.Max(4, (int)(tr.fWindowYSize - h) / 2);
		tr.DrawParchmentBox(x, y, widthUnits, heightUnits);
		bool shadows = tr.bForceDropShadows;
		tr.bForceDropShadows = true;
		Text(title, x + 28, y + 14, Palette.Accent, UiFont.Title);
		tr.bForceDropShadows = shadows;
		if (subtitle != null)
		{
			TextRight(subtitle, x + w - 84, y + 24, Palette.TextDim);
		}
		BoxButton(id + "_close", new Rectangle(x + w - 76, y + 14, 64, 32), "X", onClose, tooltipText: "Close (Esc)", font: UiFont.Medium, stackLevel: WindowLevel + 2);
		Separator(x + 24, y + 56, w - 48);
		return new Rectangle(x + 24, y + 66, w - 48, h - 66 - 20);
	}

	// ---------- dragging ----------

	private static string dragging;
	private static Point dragOffset;

	/// <summary>
	/// Makes a window draggable by a handle area. Pass the window's current top-left; returns the (possibly moved)
	/// top-left, kept on screen. onDrop runs once when the player lets go (save the position there).
	/// </summary>
	public static Point Drag(string id, Rectangle handle, Point topLeft, Point size, Action<Point> onDrop = null)
	{
		ui_mouseFrame f = Area(id + "_drag", handle, null);
		if (dragging == null && MousePressed && Hovered(f))
		{
			dragging = id;
			dragOffset = new Point(Mouse.X - topLeft.X, Mouse.Y - topLeft.Y);
		}
		if (dragging == id)
		{
			topLeft = new Point(Mouse.X - dragOffset.X, Mouse.Y - dragOffset.Y);
			if (!MouseDown)
			{
				dragging = null;
				onDrop?.Invoke(ClampToScreen(topLeft, size));
			}
		}
		if (dragging == id || Hovered(f))
		{
			Dm.Game.theInput.SetMouseIconState(MouseActionState.uipointer);
		}
		return ClampToScreen(topLeft, size);
	}

	/// <summary>True while any window is being dragged (ignore clicks then).</summary>
	public static bool IsDragging => dragging != null;

	public static Point ClampToScreen(Point topLeft, Point size)
	{
		Point s = ScreenSize;
		return new Point(Math.Max(0, Math.Min(topLeft.X, s.X - size.X)), Math.Max(0, Math.Min(topLeft.Y, s.Y - size.Y)));
	}
}
