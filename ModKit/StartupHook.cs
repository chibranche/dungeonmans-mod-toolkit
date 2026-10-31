using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// .NET startup hook entry point. The runtime calls StartupHook.Initialize() before the game's Main,
// because Dungeonmans.runtimeconfig.json lists this DLL under "STARTUP_HOOKS" (Install.bat sets that up).
// Must live in the global namespace with exactly this name and signature.
internal class StartupHook
{
	public static void Initialize()
	{
		string kitDir = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location);
		// 0Harmony.dll (and mod DLL dependencies) aren't in the game's deps.json, so resolve them ourselves:
		// first next to ModKit, then in every mod folder.
		AssemblyLoadContext.Default.Resolving += (ctx, name) =>
		{
			string candidate = Path.Combine(kitDir, name.Name + ".dll");
			if (File.Exists(candidate))
			{
				return ctx.LoadFromAssemblyPath(candidate);
			}
			return ResolveFromMods(name.Name);
		};
		try
		{
			Start();
		}
		catch (Exception e)
		{
			DungeonmansModKit.KitLog.Write("FAILED to start ModKit: " + e);
		}
	}

	// Kept separate so no Harmony type is touched before the resolver above is registered.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Start() => DungeonmansModKit.Loader.Start();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Reflection.Assembly ResolveFromMods(string name) => DungeonmansModKit.Loader.ResolveDependency(name);
}
