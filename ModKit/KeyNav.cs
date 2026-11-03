using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMans;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace DungeonmansModKit;

// Keyboard navigation for mod windows. Every clickable Area drawn inside a window (stack level >= WindowLevel)
// while a registered modal (Ui.AddModal) is open becomes a focus target:
//   Tab / Shift+Tab  next / previous target (reading order)
//   Arrow keys       nearest target in that direction (once something has focus)
//   Enter / Space    click the focused target
// A mouse click or Esc drops the focus. Only targets drawn after the last dimmed window (Ui.DimAndBlock /
// Ui.ModalWindow) are used, so buttons behind a window can't be reached. Off while a game dialog is up.
public static partial class Ui
{
	private struct NavItem
	{
		public string Id;
		public Rectangle Rect;
		public int Level;
		public int Layer;
	}

	private static readonly List<NavItem> navThisFrame = new List<NavItem>();
	private static List<NavItem> navLastFrame = new List<NavItem>();
	private static string focusId;
	private static KeyboardState prevKeys;
	private static int navLayer;

	// A new dimmed window: targets drawn before it are behind it.
	internal static void StartNavLayer() => navLayer++;

	/// <summary>True when a target has keyboard focus. Windows with their own arrow-key shortcuts should skip them then.</summary>
	public static bool KeyFocusActive => focusId != null && navLastFrame.Any(n => n.Id == focusId);

	// Where the focused target was drawn last frame (tooltips go next to it instead of the mouse).
	internal static Rectangle? FocusRect => focusId == null ? null : navLastFrame.FirstOrDefault(n => n.Id == focusId) is { Id: not null } n ? n.Rect : null;

	/// <summary>Is this Area / button the keyboard-focused one?</summary>
	public static bool IsFocused(string id) => focusId != null && focusId == id;

	internal static void RegisterNav(string id, Rectangle r, int stackLevel)
	{
		if (stackLevel >= WindowLevel)
		{
			navThisFrame.Add(new NavItem { Id = id, Rect = r, Level = stackLevel, Layer = navLayer });
		}
	}

	private static bool NavEnabled(dmGame g) => !SettingsPanel.IsOpen && !g.bDialogBoxMode && modals.Any(SafeModal);

	// Called once per frame (from BeginFrame) before anything is drawn: uses last frame's targets.
	private static void UpdateKeyNav()
	{
		navLastFrame = new List<NavItem>(navThisFrame);
		navThisFrame.Clear();
		navLayer = 0;
		dmGame g = Dm.Game;
		KeyboardState k = Keyboard.GetState();
		bool Pressed(Keys key) => k.IsKeyDown(key) && !prevKeys.IsKeyDown(key);
		try
		{
			if (g == null || !g.IsActive || !NavEnabled(g) || navLastFrame.Count == 0)
			{
				if (g != null && !NavEnabled(g))
				{
					focusId = null;
				}
				return;
			}
			if (MousePressed || Pressed(Keys.Escape))
			{
				focusId = null;
				return;
			}
			int layer = navLastFrame.Max(n => n.Layer);
			List<NavItem> items = navLastFrame.Where(n => n.Layer == layer).OrderBy(n => n.Rect.Y / 8).ThenBy(n => n.Rect.X).ToList();
			int cur = items.FindIndex(n => n.Id == focusId);
			bool shift = k.IsKeyDown(Keys.LeftShift) || k.IsKeyDown(Keys.RightShift);
			if (Pressed(Keys.Tab))
			{
				cur = cur < 0 ? (shift ? items.Count - 1 : 0) : (cur + (shift ? items.Count - 1 : 1)) % items.Count;
				focusId = items[cur].Id;
				dmAudio.PlayUIFX("ui_click_01");
				return;
			}
			if (cur < 0)
			{
				return;
			}
			Point dir = Pressed(Keys.Left) ? new Point(-1, 0) : Pressed(Keys.Right) ? new Point(1, 0)
				: Pressed(Keys.Up) ? new Point(0, -1) : Pressed(Keys.Down) ? new Point(0, 1) : Point.Zero;
			if (dir != Point.Zero)
			{
				NavItem? next = Nearest(items, items[cur].Rect, dir);
				if (next.HasValue)
				{
					focusId = next.Value.Id;
					dmAudio.PlayUIFX("ui_click_01");
				}
				return;
			}
			if ((Pressed(Keys.Enter) || Pressed(Keys.Space)) && clicks.TryGetValue(focusId, out Action a))
			{
				try
				{
					a();
				}
				catch (Exception e)
				{
					KitLog.Write("Key press on " + focusId + " threw: " + e);
				}
			}
		}
		finally
		{
			prevKeys = k;
		}
	}

	// Spatial step: the closest target whose center lies in the direction, favoring ones in line.
	private static NavItem? Nearest(List<NavItem> items, Rectangle from, Point dir)
	{
		Point c = from.Center;
		NavItem? best = null;
		double bestScore = double.MaxValue;
		foreach (NavItem n in items)
		{
			if (n.Rect == from)
			{
				continue;
			}
			Point o = n.Rect.Center;
			int along = dir.X != 0 ? (o.X - c.X) * dir.X : (o.Y - c.Y) * dir.Y;
			int across = dir.X != 0 ? Math.Abs(o.Y - c.Y) : Math.Abs(o.X - c.X);
			if (along <= 4)
			{
				continue;
			}
			double score = along + across * 2.5;
			if (score < bestScore)
			{
				bestScore = score;
				best = n;
			}
		}
		return best;
	}

	// Gold frame around the focused target, drawn above the windows.
	internal static void FlushFocus()
	{
		if (focusId == null || Dm.Renderer == null)
		{
			return;
		}
		foreach (NavItem n in navThisFrame)
		{
			if (n.Id == focusId)
			{
				Dm.Renderer.BeginSpriteBatch();
				Rectangle r = n.Rect;
				Outline(new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), Palette.Accent, 2);
				Dm.Renderer.EndSpriteBatch();
				return;
			}
		}
	}
}
