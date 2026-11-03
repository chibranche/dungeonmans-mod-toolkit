using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DungeonMans;
using Microsoft.Xna.Framework;

namespace DungeonmansModKit;

/// <summary>
/// A shared row of buttons at the top right of the screen (left of the game's own icon), for every mod. Each frame,
/// from <see cref="Events.DrawOverlay"/>, a mod adds the buttons it wants shown; ModKit lays them out right to left
/// with no gaps and draws them after all mods. The player can drag a button sideways to reorder the row; the order
/// is saved in ModKit\topbar.json and shared by all mods. Draw them with <see cref="Ui.HangingButton"/> to match.
/// </summary>
public static class TopBar
{
	private sealed class Slot
	{
		public string Id;
		public int Width;
		public int Priority;
		public Action<Rectangle, bool> Draw;
	}

	private const int Gap = 8, DragThreshold = 6;
	private static readonly string OrderPath = Path.Combine(Loader.KitDir, "topbar.json");

	private static readonly List<Slot> slots = new List<Slot>();
	private static int frame = -1;
	private static List<string> savedOrder;
	private static string pressed, dragging;
	private static int pressX, grabOffset;

	/// <summary>
	/// Shows a button in this frame's bar. id must be unique (prefix it with your mod id, e.g. "qol.speed").
	/// draw gets the button's rectangle and whether it may be clicked (false while the player drags it: draw it
	/// without a click action). priority places it the first time it is seen: lower = further right.
	/// </summary>
	public static void Add(string id, int width, Action<Rectangle, bool> draw, int priority = 100)
	{
		if (frame != Ui.Frame)
		{
			frame = Ui.Frame;
			slots.Clear();
		}
		if (draw != null && !failed.Contains(id) && slots.All(s => s.Id != id))
		{
			slots.Add(new Slot { Id = id, Width = width, Priority = priority, Draw = draw });
		}
	}

	/// <summary>True while the player is dragging a top bar button.</summary>
	public static bool IsDragging => dragging != null;

	private static List<string> SavedOrder()
	{
		if (savedOrder == null)
		{
			savedOrder = new List<string>();
			try
			{
				if (File.Exists(OrderPath))
				{
					savedOrder = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(OrderPath)) ?? new List<string>();
				}
			}
			catch (Exception e)
			{
				KitLog.Write("Could not read " + OrderPath + ": " + e.Message);
				KitFiles.KeepBad(OrderPath);
			}
		}
		return savedOrder;
	}

	private static void SaveOrder(List<string> shownOrder)
	{
		// Buttons not shown right now keep their place relative to the others.
		List<string> old = SavedOrder();
		var result = new List<string>(shownOrder);
		foreach (string id in old.Where(id => !shownOrder.Contains(id)))
		{
			int before = old.IndexOf(id);
			int at = result.FindIndex(v => old.IndexOf(v) > before);
			result.Insert(at < 0 ? result.Count : at, id);
		}
		savedOrder = result;
		try
		{
			KitFiles.WriteAtomic(OrderPath, JsonSerializer.Serialize(result));
		}
		catch (Exception e)
		{
			KitLog.Write("Could not write " + OrderPath + ": " + e.Message);
		}
	}

	// Saved buttons in their saved order; new ones by priority, after the saved ones.
	private static List<Slot> Ordered()
	{
		List<string> saved = SavedOrder();
		return slots.OrderBy(s => saved.Contains(s.Id) ? 0 : 1).ThenBy(s => saved.IndexOf(s.Id)).ThenBy(s => s.Priority).ToList();
	}

	private static List<Rectangle> Layout(List<Slot> order)
	{
		var rects = new List<Rectangle>();
		int right = Ui.ScreenSize.X - 112;
		foreach (Slot s in order)
		{
			rects.Add(new Rectangle(right - s.Width, 0, s.Width, 32));
			right -= s.Width + Gap;
		}
		return rects;
	}

	// Called by ModKit once per frame, after every mod's DrawOverlay.
	internal static void Draw(dmGame g)
	{
		if (frame != Ui.Frame || slots.Count == 0)
		{
			pressed = dragging = null;
			return;
		}
		List<Slot> order = Ordered();
		List<Rectangle> rects = Layout(order);
		Point m = Ui.Mouse;

		// Press on a button, then move sideways: it is dragged (no click on release).
		if (dragging == null && pressed == null && Ui.MousePressed && !Ui.IsDragging)
		{
			int hit = rects.FindIndex(r => r.Contains(m));
			if (hit >= 0)
			{
				pressed = order[hit].Id;
				pressX = m.X;
				grabOffset = m.X - rects[hit].X;
			}
		}
		if (pressed != null && dragging == null && Ui.MouseDown && Math.Abs(m.X - pressX) > DragThreshold && order.Any(s => s.Id == pressed))
		{
			dragging = pressed;
		}

		Slot dragged = dragging == null ? null : order.FirstOrDefault(s => s.Id == dragging);
		if (dragging != null && dragged == null)
		{
			dragging = pressed = null; // its mod stopped showing it mid-drag
		}
		if (dragged != null)
		{
			// Where it would land: the spot whose center is closest to the dragged button's center.
			List<Slot> others = order.Where(s => s != dragged).ToList();
			int dragCenter = m.X - grabOffset + dragged.Width / 2;
			int best = 0, bestDist = int.MaxValue;
			for (int k = 0; k <= others.Count; k++)
			{
				var trial = new List<Slot>(others);
				trial.Insert(k, dragged);
				int dist = Math.Abs(Layout(trial)[k].Center.X - dragCenter);
				if (dist < bestDist)
				{
					bestDist = dist;
					best = k;
				}
			}
			order = new List<Slot>(others);
			order.Insert(best, dragged);
			rects = Layout(order);
			if (!Ui.MouseDown)
			{
				SaveOrder(order.Select(s => s.Id).ToList());
				dragging = pressed = null;
				dragged = null;
			}
		}
		else if (!Ui.MouseDown)
		{
			pressed = null;
		}

		for (int i = 0; i < order.Count; i++)
		{
			if (order[i] != dragged)
			{
				DrawSlot(order[i], rects[i], dragged == null);
			}
		}
		if (dragged != null)
		{
			int x = Math.Clamp(m.X - grabOffset, 0, Ui.ScreenSize.X - dragged.Width);
			DrawSlot(dragged, new Rectangle(x, 0, dragged.Width, 32), false);
			g.theInput.SetMouseIconState(MouseActionState.uipointer);
		}
	}

	// One mod's broken button doesn't hide the others.
	private static readonly HashSet<string> failed = new HashSet<string>();

	private static void DrawSlot(Slot s, Rectangle r, bool clickable)
	{
		try
		{
			s.Draw(r, clickable);
		}
		catch (Exception e)
		{
			Ui.RecoverBatch();
			if (failed.Add(s.Id))
			{
				KitLog.Write("Top bar button " + s.Id + " threw (hidden from now on): " + e);
			}
			slots.Remove(s);
		}
	}
}
