using System;
using System.Collections.Generic;
using System.Text;
using DungeonMans;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DungeonmansModKit;

/// <summary>Colors that read well on the game's dark UI. Use them so mods look consistent.</summary>
public static class Palette
{
	public static readonly Color Panel = new Color(24, 18, 14, 215);
	public static readonly Color PanelRaised = new Color(44, 34, 26, 235);
	public static readonly Color PanelHover = new Color(70, 54, 36, 240);
	public static readonly Color PanelSelected = new Color(92, 70, 30, 245);
	public static readonly Color Border = new Color(112, 88, 60);
	public static readonly Color BorderBright = new Color(214, 176, 92);
	public static readonly Color Text = new Color(236, 228, 210);
	public static readonly Color TextDim = new Color(176, 164, 142);
	public static readonly Color TextMuted = new Color(120, 110, 96);
	public static readonly Color Accent = new Color(250, 206, 92);
	public static readonly Color Good = new Color(98, 196, 98);
	public static readonly Color Warn = new Color(232, 168, 62);
	public static readonly Color Bad = new Color(222, 76, 64);
	public static readonly Color Info = new Color(92, 186, 232);
	public static readonly Color BarBack = new Color(12, 9, 7, 230);

	/// <summary>Green at full, yellow at half, red when low.</summary>
	public static Color Health(float fraction) =>
		fraction > 0.5f ? Color.Lerp(Warn, Good, (fraction - 0.5f) * 2f) : Color.Lerp(Bad, Warn, Math.Max(0f, fraction) * 2f);
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

	// ---------- primitives ----------

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

	/// <summary>A solid rectangle (drawn in order with text and sprites, unlike the renderer's DrawLine).</summary>
	public static void Fill(Rectangle r, Color c)
	{
		if (r.Width > 0 && r.Height > 0)
		{
			Batch.Draw(Pixel, r, c);
		}
	}

	public static void Outline(Rectangle r, Color c, int thickness = 1)
	{
		Fill(new Rectangle(r.X, r.Y, r.Width, thickness), c);
		Fill(new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), c);
		Fill(new Rectangle(r.X, r.Y, thickness, r.Height), c);
		Fill(new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), c);
	}

	/// <summary>A dark box with a thin border: the base for cards, sections and list rows.</summary>
	public static void Panel(Rectangle r, Color? fill = null, Color? border = null)
	{
		Fill(r, fill ?? Palette.Panel);
		Outline(r, border ?? Palette.Border);
	}

	/// <summary>A horizontal divider line.</summary>
	public static void Separator(int x, int y, int width) => Fill(new Rectangle(x, y, width, 1), Palette.Border);

	/// <summary>A progress bar; label (optional) is centered on it.</summary>
	public static void Bar(Rectangle r, float fraction, Color fill, string label = null, UiFont font = UiFont.Small)
	{
		Fill(r, Palette.BarBack);
		fraction = MathHelper.Clamp(fraction, 0f, 1f);
		Fill(new Rectangle(r.X + 1, r.Y + 1, (int)((r.Width - 2) * fraction), r.Height - 2), fill);
		Fill(new Rectangle(r.X + 1, r.Y + 1, (int)((r.Width - 2) * fraction), Math.Max(1, (r.Height - 2) / 3)), new Color(255, 255, 255, 40));
		Outline(r, Palette.Border);
		if (label != null)
		{
			TextCentered(label, r, Palette.Text, font);
		}
	}

	/// <summary>
	/// A bar centered on 0 for a value in [-max, max], drawn as pips: negative values fill left in negColor,
	/// positive values fill right in posColor.
	/// </summary>
	public static void CenterBar(Rectangle r, int value, int max, Color negColor, Color posColor)
	{
		Fill(r, Palette.BarBack);
		int n = max * 2;
		float pip = (r.Width - 2) / (float)n;
		for (int i = 0; i < n; i++)
		{
			int px = r.X + 1 + (int)(i * pip);
			var cell = new Rectangle(px + 1, r.Y + 2, Math.Max(1, (int)pip - 2), r.Height - 4);
			int slot = i < max ? i - max : i - max + 1; // -max..-1, 1..max
			bool on = value < 0 ? slot < 0 && slot >= value : value > 0 && slot > 0 && slot <= value;
			Fill(cell, on ? (slot < 0 ? negColor : posColor) : new Color(60, 48, 38, 200));
		}
		Fill(new Rectangle(r.X + r.Width / 2, r.Y - 2, 1, r.Height + 4), Palette.TextDim);
		Outline(r, Palette.Border);
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

	private static readonly Dictionary<string, dmSprite> sprites = new Dictionary<string, dmSprite>();

	/// <summary>
	/// Draws one of the game's sprites by name, fitted in a box (e.g. "ui_favorite_heart", "ui_lock", "ui_attack_block",
	/// "condition_regen", "ui_levelup", "single_star_font"; names are in Content\SpriteData\*.txt).
	/// </summary>
	public static void Icon(string spriteName, Rectangle box, Color? tint = null)
	{
		if (!sprites.TryGetValue(spriteName, out dmSprite sp))
		{
			try
			{
				sp = Dm.Renderer.GetSpriteByName(spriteName);
			}
			catch
			{
				sp = default;
			}
			sprites[spriteName] = sp;
		}
		Sprite(sp, box, tint);
	}

	/// <summary>An on/off switch (pill with a knob). Returns true while hovered.</summary>
	public static bool Switch(string id, Rectangle r, bool on, Action onToggle, string tooltipText = null, int stackLevel = WindowLevel + 1)
	{
		ui_mouseFrame f = Area(id, r, () => onToggle?.Invoke(), stackLevel);
		bool hot = Hovered(f);
		Color track = on ? new Color(52, 120, 60, 240) : new Color(56, 48, 42, 240);
		Fill(r, track);
		Outline(r, hot ? Palette.BorderBright : on ? Palette.Good : Palette.Border);
		int k = r.Height - 6;
		var knob = new Rectangle(on ? r.Right - k - 3 : r.X + 3, r.Y + 3, k, k);
		Fill(knob, on ? new Color(220, 240, 210) : Palette.TextDim);
		if (hot && tooltipText != null)
		{
			Tooltip(tooltipText);
		}
		return hot;
	}

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

	public static int LineHeight(UiFont font) => font == UiFont.Large ? 34 : font == UiFont.Medium ? 26 : 20;

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
	/// A boxed button with hover/selected states and an optional tooltip. Returns true while hovered.
	/// </summary>
	public static bool BoxButton(string id, Rectangle r, string label, Action onClick, bool enabled = true, bool selected = false,
		UiFont font = UiFont.Small, string tooltipText = null, Color? accent = null, int stackLevel = WindowLevel + 1)
	{
		ui_mouseFrame f = Area(id, r, enabled ? () => { dmAudio.PlayUIFX("ui_click_01"); onClick?.Invoke(); } : null, stackLevel);
		bool hot = Hovered(f);
		Color fill = !enabled ? new Color(30, 26, 22, 200) : selected ? Palette.PanelSelected : hot ? Palette.PanelHover : Palette.PanelRaised;
		Color border = !enabled ? Palette.TextMuted : selected || hot ? (accent ?? Palette.BorderBright) : Palette.Border;
		Fill(r, fill);
		Outline(r, border);
		Color text = !enabled ? Palette.TextMuted : selected ? Palette.Accent : hot ? Color.White : (accent ?? Palette.Text);
		TextCentered(label, r, text, font);
		if (hot && tooltipText != null)
		{
			Tooltip(tooltipText);
		}
		return hot;
	}

	/// <summary>Width a BoxButton needs for a label.</summary>
	public static int ButtonWidth(string label, UiFont font = UiFont.Small) => (int)Measure(label, font).X + 20;

	/// <summary>A small rounded "pill" label (status, tag). Draws at x and returns the x after it (plus a gap).</summary>
	public static int Chip(int x, int y, string text, Color color, UiFont font = UiFont.Small)
	{
		Vector2 size = Measure(text, font);
		var r = new Rectangle(x, y, (int)size.X + 12, LineHeight(font) + 2);
		Fill(r, new Color(color.R / 4, color.G / 4, color.B / 4, 220));
		Outline(r, color);
		TextCentered(text, r, color, font);
		return r.Right + 6;
	}

	/// <summary>
	/// Row of tabs. Returns the tab row's height. Disabled tabs (enabled[i] == false) are grayed out.
	/// </summary>
	public static int Tabs(string id, int x, int y, string[] labels, int current, Action<int> onPick, bool[] enabled = null, UiFont font = UiFont.Medium)
	{
		int h = LineHeight(font) + 10;
		for (int i = 0; i < labels.Length; i++)
		{
			int idx = i;
			int w = Math.Max(96, ButtonWidth(labels[i], font) + 16);
			BoxButton(id + "_tab" + i, new Rectangle(x, y, w, h), labels[i], () => onPick(idx), enabled == null || enabled[i], i == current, font);
			x += w + 4;
		}
		return h;
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
		int h = lines.Length * LineHeight(UiFont.Small);
		Point m = Mouse;
		var r = new Rectangle(m.X + 18, m.Y + 18, w + 20, h + 12);
		if (r.Right > tr.fWindowXSize - 4)
		{
			r.X = m.X - r.Width - 8;
		}
		if (r.Bottom > tr.fWindowYSize - 4)
		{
			r.Y = m.Y - r.Height - 8;
		}
		tr.BeginSpriteBatch();
		Fill(r, new Color(14, 10, 8, 245));
		Outline(r, Palette.BorderBright);
		for (int i = 0; i < lines.Length; i++)
		{
			Text(lines[i], r.X + 10, r.Y + 6 + i * LineHeight(UiFont.Small), Palette.Text);
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
		int y = 64;
		foreach (ToastItem t in toasts)
		{
			float left = (float)(t.Until - DateTime.Now).TotalSeconds;
			byte a = (byte)(255 * MathHelper.Clamp(left / 0.4f, 0f, 1f));
			Vector2 size = Measure(t.Text, UiFont.Medium);
			var r = new Rectangle((int)(tr.fWindowXSize - size.X) / 2 - 18, y, (int)size.X + 36, 38);
			Fill(r, new Color((byte)14, (byte)10, (byte)8, (byte)(a * 0.92f)));
			Fill(new Rectangle(r.X, r.Y, 4, r.Height), new Color(t.Accent.R, t.Accent.G, t.Accent.B, a));
			Outline(r, new Color(Palette.Border.R, Palette.Border.G, Palette.Border.B, a));
			TextCentered(t.Text, r, new Color(Palette.Text.R, Palette.Text.G, Palette.Text.B, a), UiFont.Medium);
			y += 44;
		}
		tr.EndSpriteBatch();
	}

	// ---------- windows ----------

	/// <summary>
	/// The standard modal window: dims the screen, blocks clicks behind it, draws a centered parchment box with a
	/// title bar (icon character optional), a subtitle on the right and a close [X]. Returns the content rectangle
	/// below the title bar. Call inside BeginSpriteBatch/EndSpriteBatch.
	/// </summary>
	public static Rectangle ModalWindow(string id, int widthUnits, int heightUnits, string title, Action onClose, string subtitle = null)
	{
		wg2DRenderer tr = Dm.Renderer;
		DimAndBlock(id);
		int w = widthUnits * 32, h = heightUnits * 32 + 32;
		int x = (int)(tr.fWindowXSize - w) / 2;
		int y = Math.Max(4, (int)(tr.fWindowYSize - h) / 2);
		tr.DrawParchmentBox(x, y, widthUnits, heightUnits);
		var bar = new Rectangle(x + 14, y + 12, w - 28, 40);
		Fill(bar, new Color(0, 0, 0, 90));
		Fill(new Rectangle(bar.X, bar.Bottom, bar.Width, 2), Palette.BorderBright);
		Text(title, bar.X + 12, bar.Y + 3, Palette.Accent, UiFont.Large);
		if (subtitle != null)
		{
			TextRight(subtitle, bar.Right - 52, bar.Y + 11, Palette.TextDim);
		}
		BoxButton(id + "_close", new Rectangle(bar.Right - 40, bar.Y + 5, 32, 30), "X", onClose, tooltipText: "Close (Esc)", stackLevel: WindowLevel + 2);
		return new Rectangle(x + 18, bar.Bottom + 10, w - 36, y + h - 20 - (bar.Bottom + 10));
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
