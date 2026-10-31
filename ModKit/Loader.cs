using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using HarmonyLib;

namespace DungeonmansModKit;

/// <summary>Shared log file: ModKit\modkit.log (the previous session is kept as modkit.prev.log).</summary>
public static class KitLog
{
	private static readonly string LogPath = Path.Combine(Loader.KitDir, "modkit.log");
	private static bool rotated;

	public static void Write(string msg)
	{
		try
		{
			if (!rotated)
			{
				rotated = true;
				if (File.Exists(LogPath))
				{
					File.Copy(LogPath, Path.Combine(Loader.KitDir, "modkit.prev.log"), true);
					File.Delete(LogPath);
				}
			}
			File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
		}
		catch
		{
		}
	}
}

/// <summary>A mod found in the Mods folder, whether it loaded or not.</summary>
public sealed class LoadedMod
{
	public ModInfo Info { get; internal set; }
	public string Folder { get; internal set; }
	public ModContext Context { get; internal set; }
	/// <summary>"loaded", "disabled", "failed" or "no code" (data only).</summary>
	public string State { get; internal set; }
	public string Error { get; internal set; }
	public bool Enabled => !Loader.IsDisabled(Info.Id);
}

/// <summary>Finds mods in &lt;game&gt;\Mods, compiles or loads their code and calls their <see cref="IMod.Load"/>.</summary>
public static class Loader
{
	public static readonly Version KitVersion = typeof(Loader).Assembly.GetName().Version;

	public static string KitDir { get; } = Path.GetDirectoryName(typeof(Loader).Assembly.Location) ?? ".";
	public static string GameDir { get; } = AppContext.BaseDirectory;
	public static string ModsDir { get; } = Path.Combine(AppContext.BaseDirectory, "Mods");

	private static readonly List<LoadedMod> mods = new List<LoadedMod>();
	public static IReadOnlyList<LoadedMod> Mods => mods;

	private static readonly string KitConfigPath = Path.Combine(KitDir, "modkit.json");

	private sealed class KitConfig
	{
		public List<string> Disabled { get; set; } = new List<string>();
	}

	private static KitConfig config;

	internal static void Start()
	{
		KitLog.Write($"Dungeonmans ModKit {KitVersion.ToString(3)} starting (game: {GameDir})");
		config = LoadConfig();
		var core = new Harmony("modkit.core");
		try
		{
			CoreHooks.Register(core);
			KitLog.Write("  ok   ModKit core hooks");
		}
		catch (Exception e)
		{
			KitLog.Write("  FAIL ModKit core hooks (Mod Settings menu and mod events won't work): " + e);
		}
		Directory.CreateDirectory(ModsDir);
		foreach (string folder in Directory.GetDirectories(ModsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
		{
			LoadMod(folder);
		}
		KitLog.Write($"done: {mods.Count(m => m.State == "loaded")} loaded, {mods.Count(m => m.State == "disabled")} disabled, {mods.Count(m => m.State == "failed")} failed");
	}

	private static ModInfo ReadInfo(string folder)
	{
		string path = Path.Combine(folder, "mod.json");
		ModInfo info = null;
		if (File.Exists(path))
		{
			info = JsonSerializer.Deserialize<ModInfo>(File.ReadAllText(path),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
		}
		info ??= new ModInfo();
		string folderName = Path.GetFileName(folder);
		if (string.IsNullOrWhiteSpace(info.Id))
		{
			info.Id = folderName.ToLowerInvariant().Replace(' ', '_');
		}
		if (string.IsNullOrWhiteSpace(info.Name))
		{
			info.Name = folderName;
		}
		return info;
	}

	private static void LoadMod(string folder)
	{
		var mod = new LoadedMod { Folder = folder };
		try
		{
			mod.Info = ReadInfo(folder);
		}
		catch (Exception e)
		{
			mod.Info = new ModInfo { Id = Path.GetFileName(folder).ToLowerInvariant(), Name = Path.GetFileName(folder) };
			mod.State = "failed";
			mod.Error = "bad mod.json: " + e.Message;
			mods.Add(mod);
			KitLog.Write($"{mod.Info.Name}: FAIL {mod.Error}");
			return;
		}
		mods.Add(mod);
		ModInfo info = mod.Info;
		if (mods.Count(m => m.Info.Id == info.Id) > 1)
		{
			mod.State = "failed";
			mod.Error = "another mod already uses the id '" + info.Id + "'";
			KitLog.Write($"{info.Name}: FAIL {mod.Error}");
			return;
		}
		if (IsDisabled(info.Id))
		{
			mod.State = "disabled";
			KitLog.Write($"{info.Name} {info.Version}: disabled in Mod Settings");
			return;
		}
		if (info.MinModKitVersion != null && Version.TryParse(info.MinModKitVersion, out Version need) && need > KitVersion)
		{
			mod.State = "failed";
			mod.Error = $"needs ModKit {info.MinModKitVersion} or newer (this is {KitVersion.ToString(3)})";
			KitLog.Write($"{info.Name}: FAIL {mod.Error}");
			return;
		}
		KitLog.Write($"{info.Name} {info.Version} ({info.Id})");
		try
		{
			var assemblies = new List<Assembly>();
			foreach (string dll in Directory.GetFiles(folder, "*.dll"))
			{
				assemblies.Add(LoadDll(dll));
			}
			string codeDir = Path.Combine(folder, "code");
			if (Directory.Exists(codeDir) && Directory.GetFiles(codeDir, "*.cs", SearchOption.AllDirectories).Length > 0)
			{
				assemblies.Add(ScriptCompiler.Compile(info, codeDir, Path.Combine(folder, ".cache")));
			}
			mod.Context = new ModContext(info, folder);
			List<Type> entries = assemblies.SelectMany(SafeTypes)
				.Where(t => typeof(IMod).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface).ToList();
			if (entries.Count == 0)
			{
				mod.State = "no code";
				KitLog.Write("  (no IMod class found: data-only mod)");
				return;
			}
			foreach (Type t in entries)
			{
				((IMod)Activator.CreateInstance(t)).Load(mod.Context);
			}
			mod.State = "loaded";
		}
		catch (Exception e)
		{
			mod.State = "failed";
			mod.Error = e is ScriptCompiler.CompileException ? e.Message : e.ToString();
			KitLog.Write("  FAIL " + mod.Error);
		}
	}

	private static Assembly LoadDll(string path)
	{
		AssemblyName name = AssemblyName.GetAssemblyName(path);
		// Reuse an already-loaded copy (e.g. a mod that ships its own 0Harmony.dll).
		Assembly existing = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name);
		return existing ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
	}

	private static IEnumerable<Type> SafeTypes(Assembly a)
	{
		try
		{
			return a.GetTypes();
		}
		catch (ReflectionTypeLoadException e)
		{
			return e.Types.Where(t => t != null);
		}
	}

	internal static Assembly ResolveDependency(string name)
	{
		if (!Directory.Exists(ModsDir))
		{
			return null;
		}
		foreach (string folder in Directory.GetDirectories(ModsDir))
		{
			string candidate = Path.Combine(folder, name + ".dll");
			if (File.Exists(candidate))
			{
				return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(candidate));
			}
		}
		return null;
	}

	// ---------- enable / disable (takes effect at next launch) ----------

	public static bool IsDisabled(string id) => config?.Disabled.Contains(id) ?? false;

	public static void SetEnabled(string id, bool enabled)
	{
		config.Disabled.Remove(id);
		if (!enabled)
		{
			config.Disabled.Add(id);
		}
		try
		{
			File.WriteAllText(KitConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception e)
		{
			KitLog.Write("Could not write " + KitConfigPath + ": " + e.Message);
		}
	}

	private static KitConfig LoadConfig()
	{
		try
		{
			if (File.Exists(KitConfigPath))
			{
				return JsonSerializer.Deserialize<KitConfig>(File.ReadAllText(KitConfigPath)) ?? new KitConfig();
			}
		}
		catch (Exception e)
		{
			KitLog.Write("Could not read " + KitConfigPath + ": " + e.Message);
		}
		return new KitConfig();
	}
}
