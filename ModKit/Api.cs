using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DungeonMans;
using HarmonyLib;

namespace DungeonmansModKit;

/// <summary>
/// Entry point of a mod. ModKit creates one instance of every public class implementing this interface
/// (in a mod DLL or a mod's code\*.cs files) and calls <see cref="Load"/> once, before the game starts.
/// </summary>
public interface IMod
{
	void Load(ModContext ctx);
}

/// <summary>What a mod's mod.json says about it.</summary>
public sealed class ModInfo
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Version { get; set; } = "1.0.0";
	public string Author { get; set; } = "";
	public string Description { get; set; } = "";
	/// <summary>Lowest ModKit version this mod needs, e.g. "1.0.0". Optional.</summary>
	public string MinModKitVersion { get; set; }
}

/// <summary>Everything a mod gets from ModKit: its folder, a Harmony instance, logging, settings and saved data.</summary>
public sealed class ModContext
{
	internal ModContext(ModInfo info, string folder)
	{
		Info = info;
		Folder = folder;
		Harmony = new Harmony("modkit." + info.Id);
		Log = new ModLog(info.Id);
		Settings = new ModSettings(this);
		Data = new ModData(info.Id);
	}

	public ModInfo Info { get; }
	/// <summary>The mod's folder (Mods\&lt;folder&gt;). Put your own files here.</summary>
	public string Folder { get; }
	/// <summary>Harmony instance for this mod (id "modkit.&lt;mod id&gt;").</summary>
	public Harmony Harmony { get; }
	public ModLog Log { get; }
	/// <summary>On/off switches shown in the Mod Settings menu, saved in the mod folder's config.json.</summary>
	public ModSettings Settings { get; }
	/// <summary>Values saved inside the player's save (survive deaths and NG+).</summary>
	public ModData Data { get; }

	internal readonly List<(string Name, bool Ok)> Patches = new List<(string, bool)>();

	/// <summary>Full path of a file in the mod folder.</summary>
	public string PathFor(string fileName) => Path.Combine(Folder, fileName);

	/// <summary>
	/// Runs one group of Harmony patches. If it throws (usually because a game update renamed something),
	/// the error is logged as "FAIL name" and the rest of the mod keeps loading.
	/// </summary>
	public bool Patch(string name, Action<Harmony> register)
	{
		try
		{
			register(Harmony);
			Log.Write("  ok   " + name);
			Patches.Add((name, true));
			return true;
		}
		catch (Exception e)
		{
			Log.Write("  FAIL " + name + ": " + e);
			Patches.Add((name, false));
			return false;
		}
	}
}

/// <summary>Writes to ModKit\modkit.log, prefixed with the mod id.</summary>
public sealed class ModLog
{
	private readonly string prefix;

	internal ModLog(string id) => prefix = "[" + id + "] ";

	public void Write(string message) => KitLog.Write(prefix + message);
}

/// <summary>A switch shown in the Mod Settings menu.</summary>
public sealed class ModToggle
{
	public string Key { get; internal set; }
	public string Label { get; internal set; }
	public string Description { get; internal set; }
	public bool Default { get; internal set; }
	/// <summary>Heading this switch is listed under in Mod Settings (optional).</summary>
	public string Group { get; internal set; }
}

/// <summary>
/// Per-mod on/off settings. Register switches with <see cref="AddToggle"/> so they appear in the Mod Settings menu;
/// read them with <see cref="On"/> every time your code runs, so changes apply immediately.
/// Values are stored in the mod folder's config.json. Unregistered keys work too (hidden from the menu),
/// which is handy for remembering UI state.
/// </summary>
public sealed class ModSettings
{
	private readonly ModContext ctx;
	private readonly List<ModToggle> toggles = new List<ModToggle>();
	private Dictionary<string, object> values;

	internal ModSettings(ModContext ctx) => this.ctx = ctx;

	public IReadOnlyList<ModToggle> Toggles => toggles;

	private string ConfigPath => ctx.PathFor("config.json");

	public ModToggle AddToggle(string key, string label, string description = "", bool defaultOn = true, string group = null)
	{
		var t = new ModToggle { Key = key, Label = label, Description = description, Default = defaultOn, Group = group };
		toggles.Add(t);
		return t;
	}

	public bool On(string key)
	{
		if (Values.TryGetValue(key, out object v) && v is bool on)
		{
			return on;
		}
		return toggles.FirstOrDefault(t => t.Key == key)?.Default ?? true;
	}

	public void Set(string key, bool on)
	{
		Values[key] = on;
		Save();
	}

	public void Toggle(string key) => Set(key, !On(key));

	/// <summary>A whole-number setting (e.g. a window position), saved with the switches.</summary>
	public int GetInt(string key, int fallback = 0) => Values.TryGetValue(key, out object v) && v is int i ? i : fallback;

	public void SetInt(string key, int value)
	{
		Values[key] = value;
		Save();
	}

	private Dictionary<string, object> Values => values ??= Load();

	private Dictionary<string, object> Load()
	{
		var result = new Dictionary<string, object>();
		try
		{
			if (File.Exists(ConfigPath))
			{
				var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(ConfigPath));
				foreach (var kv in raw ?? new Dictionary<string, JsonElement>())
				{
					switch (kv.Value.ValueKind)
					{
					case JsonValueKind.True:
					case JsonValueKind.False:
						result[kv.Key] = kv.Value.GetBoolean();
						break;
					case JsonValueKind.Number when kv.Value.TryGetInt32(out int n):
						result[kv.Key] = n;
						break;
					}
				}
			}
		}
		catch (Exception e)
		{
			ctx.Log.Write("Could not read " + ConfigPath + ": " + e.Message);
		}
		return result;
	}

	private void Save()
	{
		try
		{
			File.WriteAllText(ConfigPath, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception e)
		{
			ctx.Log.Write("Could not write " + ConfigPath + ": " + e.Message);
		}
	}
}

/// <summary>
/// Values stored in the player's save file. Account-wide values live in the overworld's script variables, so they
/// survive the hero dying and New Game+; <see cref="HeroString"/> values belong to the current hero only.
/// Keys are prefixed with "&lt;mod id&gt;_" so mods can't collide.
/// </summary>
public sealed class ModData
{
	private const string AccountMap = "overworld";
	private readonly string prefix;

	internal ModData(string id) => prefix = id + "_";

	public string GetString(string key, string fallback = "")
	{
		string v = dmGazetteer.GetMapScriptVar_String(prefix + key, AccountMap);
		return string.IsNullOrEmpty(v) ? fallback : v;
	}

	public void SetString(string key, string value) => dmGazetteer.SetMapScriptVar_String(prefix + key, value ?? "", AccountMap);

	public int GetInt(string key) => dmGazetteer.GetMapScriptVar_Int(prefix + key, AccountMap);

	public void SetInt(string key, int value) => dmGazetteer.SetMapScriptVar_Int(prefix + key, value, AccountMap);

	/// <summary>A value on the current hero (lost when the hero dies). Empty string if unset or no hero.</summary>
	public string HeroString(string key) => dmUtilities.theGame?.dungeonMans?.GetScriptVar_String(prefix + key) ?? "";

	public void SetHeroString(string key, string value) => dmUtilities.theGame?.dungeonMans?.SetScriptVar_String(prefix + key, value ?? "");
}
