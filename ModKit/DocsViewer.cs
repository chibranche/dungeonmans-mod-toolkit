using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DungeonMans;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonmansModKit;

// A mod's documentation, opened from its card in Mod Settings ("Docs"). Pages are the .md files in the mod's docs\
// folder, in file-name order (01-intro.md, 02-...). A small Markdown subset is understood:
//   # Page title        ## Heading        - bullet        blank line = new paragraph
//   ![caption](picture.png)   an image from the docs folder, scaled to fit
//   **bold** and `keys` show in gold; the game's own ^3 color codes work too.
internal static class DocsViewer
{
	private enum Kind
	{
		Heading,
		Paragraph,
		Bullet,
		Image,
	}

	private sealed class Block
	{
		public Kind Kind;
		public string Text;
		public string Image;
	}

	private sealed class Page
	{
		public string Title;
		public List<Block> Blocks = new List<Block>();
	}

	// One drawable line or picture, after wrapping to the page width.
	private sealed class Item
	{
		public int Height;
		public Action<int, int> Draw; // (x, y)
	}

	private static LoadedMod mod;
	private static List<Page> pages = new List<Page>();
	private static int page, scroll;
	private static List<Item> items;
	private static int itemsPage = -1, itemsWidth;
	private static readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();

	public static bool IsOpen => mod != null;

	private static string DocsDir(LoadedMod m) => Path.Combine(m.Folder, "docs");

	public static bool HasDocs(LoadedMod m)
	{
		try
		{
			return Directory.Exists(DocsDir(m)) && Directory.GetFiles(DocsDir(m), "*.md").Length > 0;
		}
		catch
		{
			return false;
		}
	}

	public static void Open(LoadedMod m)
	{
		try
		{
			pages = Directory.GetFiles(DocsDir(m), "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(Parse).ToList();
		}
		catch (Exception e)
		{
			KitLog.Write("Docs of " + m.Info.Name + " could not be read: " + e.Message);
			return;
		}
		foreach (Texture2D t in images.Values)
		{
			t?.Dispose();
		}
		images.Clear();
		mod = m;
		page = scroll = 0;
		itemsPage = -1;
		dmAudio.PlayUIFX("open_dialog");
	}

	public static void Close()
	{
		mod = null;
		foreach (Texture2D t in images.Values)
		{
			t?.Dispose();
		}
		images.Clear();
		dmAudio.PlayUIFX("ui_back_02");
	}

	private static void GoTo(int p)
	{
		p = Math.Clamp(p, 0, pages.Count - 1);
		if (p != page)
		{
			page = p;
			scroll = 0;
			dmAudio.PlayUIFX("ui_click_01");
		}
	}

	// ---------- parsing ----------

	private static string Inline(string s) =>
		Regex.Replace(Regex.Replace(s, @"\*\*(.+?)\*\*", "^3$1^7"), @"`(.+?)`", "^3$1^7");

	private static Page Parse(string file)
	{
		var p = new Page { Title = Path.GetFileNameWithoutExtension(file) };
		string para = null;
		void Flush()
		{
			if (para != null)
			{
				p.Blocks.Add(new Block { Kind = Kind.Paragraph, Text = Inline(para) });
				para = null;
			}
		}
		foreach (string raw in File.ReadAllLines(file))
		{
			string line = raw.Trim();
			Match img = Regex.Match(line, @"^!\[(.*?)\]\((.+?)\)$");
			if (line.Length == 0)
			{
				Flush();
			}
			else if (line.StartsWith("# "))
			{
				Flush();
				p.Title = line.Substring(2).Trim();
			}
			else if (line.StartsWith("## "))
			{
				Flush();
				p.Blocks.Add(new Block { Kind = Kind.Heading, Text = Inline(line.Substring(3).Trim()) });
			}
			else if (line.StartsWith("- ") || line.StartsWith("* "))
			{
				Flush();
				p.Blocks.Add(new Block { Kind = Kind.Bullet, Text = Inline(line.Substring(2).Trim()) });
			}
			else if (img.Success)
			{
				Flush();
				p.Blocks.Add(new Block { Kind = Kind.Image, Image = img.Groups[2].Value, Text = Inline(img.Groups[1].Value) });
			}
			else if (raw.StartsWith("  ") && p.Blocks.Count > 0 && p.Blocks[^1].Kind == Kind.Bullet && para == null)
			{
				p.Blocks[^1].Text += " " + Inline(line); // a bullet continued on the next line
			}
			else
			{
				para = para == null ? line : para + " " + line;
			}
		}
		Flush();
		return p;
	}

	private static Texture2D Image(string name)
	{
		if (!images.TryGetValue(name, out Texture2D t))
		{
			try
			{
				using FileStream fs = File.OpenRead(Path.Combine(DocsDir(mod), name));
				t = Texture2D.FromStream(Dm.Game.GraphicsDevice, fs);
			}
			catch (Exception e)
			{
				KitLog.Write("Docs image " + name + " could not be loaded: " + e.Message);
				t = null;
			}
			images[name] = t;
		}
		return t;
	}

	// ---------- layout ----------

	private static List<Item> Layout(Page p, int width, int maxImageH)
	{
		var list = new List<Item>();
		const int lineH = 26;
		foreach (Block b in p.Blocks)
		{
			switch (b.Kind)
			{
			case Kind.Heading:
				list.Add(new Item { Height = 12, Draw = (x, y) => { } });
				list.Add(new Item { Height = 34, Draw = (x, y) => Ui.Text(b.Text, x, y, Palette.Accent, UiFont.Large) });
				break;
			case Kind.Paragraph:
				foreach (string l in Ui.Wrap(b.Text, width, UiFont.Medium))
				{
					list.Add(new Item { Height = lineH, Draw = (x, y) => Ui.Text(l, x, y, Palette.Text, UiFont.Medium) });
				}
				list.Add(new Item { Height = 10, Draw = (x, y) => { } });
				break;
			case Kind.Bullet:
				List<string> lines = Ui.Wrap(b.Text, width - 24, UiFont.Medium);
				for (int i = 0; i < lines.Count; i++)
				{
					string l = lines[i];
					bool first = i == 0;
					list.Add(new Item
					{
						Height = lineH,
						Draw = (x, y) =>
						{
							if (first)
							{
								Ui.Fill(new Rectangle(x + 6, y + 11, 6, 6), Palette.Accent);
							}
							Ui.Text(l, x + 24, y, Palette.Text, UiFont.Medium);
						},
					});
				}
				break;
			case Kind.Image:
				Texture2D tex = Image(b.Image);
				if (tex == null)
				{
					list.Add(new Item { Height = lineH, Draw = (x, y) => Ui.Text("[missing picture: " + b.Image + "]", x, y, Palette.Bad, UiFont.Medium) });
					break;
				}
				float scale = Math.Min(1f, Math.Min(width / (float)tex.Width, maxImageH / (float)tex.Height));
				int w = (int)(tex.Width * scale), h = (int)(tex.Height * scale);
				list.Add(new Item { Height = 8, Draw = (x, y) => { } });
				list.Add(new Item
				{
					Height = h + 4,
					Draw = (x, y) =>
					{
						var r = new Rectangle(x + (width - w) / 2, y, w, h);
						Ui.DrawTexture(tex, r);
						Ui.Outline(new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), Palette.Border, 2);
					},
				});
				if (!string.IsNullOrEmpty(b.Text))
				{
					foreach (string l in Ui.Wrap(b.Text, width))
					{
						list.Add(new Item { Height = 22, Draw = (x, y) => Ui.TextCentered(l, new Rectangle(x, y, width, 22), Palette.TextDim) });
					}
				}
				list.Add(new Item { Height = 12, Draw = (x, y) => { } });
				break;
			}
		}
		return list;
	}

	// ---------- input ----------

	public static void HandleInput(List<ActionButton> a)
	{
		if (a.Contains(ActionButton.Close))
		{
			Close();
		}
		else if (a.Contains(ActionButton.Left) || a.Contains(ActionButton.ArrowKey_Left))
		{
			GoTo(page - 1);
		}
		else if (a.Contains(ActionButton.Right) || a.Contains(ActionButton.ArrowKey_Right))
		{
			GoTo(page + 1);
		}
		else if (a.Contains(ActionButton.MouseWheel_Up) || a.Contains(ActionButton.Up) || a.Contains(ActionButton.ArrowKey_Up))
		{
			scroll -= 3;
		}
		else if (a.Contains(ActionButton.MouseWheel_Down) || a.Contains(ActionButton.Down) || a.Contains(ActionButton.ArrowKey_Down))
		{
			scroll += 3;
		}
		a.Clear();
	}

	// ---------- drawing ----------

	public static void Draw(dmGame g)
	{
		wg2DRenderer tr = g.theRenderer;
		int wu = Math.Min(40, ((int)tr.fWindowXSize - 64) / 32);
		int hu = Math.Min(30, ((int)tr.fWindowYSize - 48) / 32);
		tr.BeginSpriteBatch();
		tr.bForceDropShadows = true;
		Rectangle c = Ui.ModalWindow("modkit_docs", wu, hu, mod.Info.Name, Close, "page " + (page + 1) + " / " + pages.Count);

		// Page list on the left.
		const int listW = 240;
		for (int i = 0; i < pages.Count; i++)
		{
			int idx = i;
			var r = new Rectangle(c.X, c.Y + i * 40, listW, 36);
			ui_mouseFrame f = Ui.Area("modkit_docs_page" + i, r, () => GoTo(idx), Ui.WindowLevel + 1);
			bool on = i == page, hot = Ui.Hovered(f);
			Ui.Box(r, on || hot);
			Ui.Text(Ui.Fit(pages[i].Title, listW - 20, UiFont.Medium), r.X + 10, r.Y + 5, on ? Palette.Accent : Palette.Text, UiFont.Medium);
		}

		// The page.
		var body = new Rectangle(c.X + listW + 24, c.Y, c.Width - listW - 24, c.Height - 44);
		Page p = pages[page];
		if (itemsPage != page || itemsWidth != body.Width - 20)
		{
			itemsPage = page;
			itemsWidth = body.Width - 20;
			// Pictures at most half the view tall, so one fits below a few lines of text.
			items = Layout(p, itemsWidth, (body.Height - 50) / 2);
		}
		Ui.Text(p.Title, body.X, body.Y, Palette.Accent, UiFont.Title);
		int top = body.Y + 50, viewH = body.Bottom - top;
		// Scrolling is by item; the last screen keeps the end of the page at the bottom.
		int lastStart = items.Count;
		for (int h = 0; lastStart > 0 && h + items[lastStart - 1].Height <= viewH; lastStart--)
		{
			h += items[lastStart - 1].Height;
		}
		scroll = Math.Clamp(scroll, 0, lastStart);
		int y = top, shown = scroll;
		for (; shown < items.Count && y + items[shown].Height <= body.Bottom; shown++)
		{
			items[shown].Draw(body.X, y);
			y += items[shown].Height;
		}
		if (shown < items.Count)
		{
			Ui.TextRight("^3v^7 more below: scroll", body.Right - 14, body.Bottom - 20, Palette.TextDim);
		}
		if (lastStart > 0)
		{
			var track = new Rectangle(body.Right - 6, top, 6, viewH);
			Ui.Fill(track, Palette.BarBack);
			int thumbH = Math.Max(24, viewH * viewH / Math.Max(viewH, items.Sum(it => it.Height)));
			int thumbY = track.Y + (int)((track.Height - thumbH) * (scroll / (float)lastStart));
			Ui.Fill(new Rectangle(track.X, thumbY, track.Width, thumbH), Palette.Border);
		}

		// Footer.
		int by = c.Bottom - 34;
		Ui.Separator(c.X, by - 8, c.Width);
		Ui.BoxButton("modkit_docs_prev", new Rectangle(body.X, by, 128, 32), "< Previous", () => GoTo(page - 1), page > 0, stackLevel: Ui.WindowLevel + 2);
		Ui.BoxButton("modkit_docs_next", new Rectangle(body.Right - 128, by, 128, 32), "Next >", () => GoTo(page + 1), page < pages.Count - 1, stackLevel: Ui.WindowLevel + 2);
		Ui.TextCentered("^3Left/Right^7 pages   ^3wheel / Up/Down^7 scroll   ^3Esc^7 back", new Rectangle(body.X + 136, by, body.Width - 272, 32), Palette.TextDim);
		tr.EndSpriteBatch();
	}
}
