using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DungeonmansModKit;

/// <summary>
/// Compiles a mod's code\*.cs files with the Roslyn compiler that ships with the game, so mods need no
/// Visual Studio or .NET SDK. The result is cached in the mod's .cache folder and only rebuilt when a file changes.
/// </summary>
internal static class ScriptCompiler
{
	public sealed class CompileException : Exception
	{
		public CompileException(string message) : base(message)
		{
		}
	}

	// Added to every .cs mod so small scripts don't need a wall of usings.
	private const string GlobalUsings =
		"global using System;\n" +
		"global using System.Collections.Generic;\n" +
		"global using System.Linq;\n" +
		"global using DungeonMans;\n" +
		"global using DungeonmansModKit;\n" +
		"global using HarmonyLib;\n" +
		"global using Microsoft.Xna.Framework;\n";

	public static Assembly Compile(ModInfo info, string codeDir, string cacheDir)
	{
		string[] files = Directory.GetFiles(codeDir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray();
		string hash = Hash(files);
		string assemblyName = "ModKitScript." + info.Id;
		string cached = Path.Combine(cacheDir, assemblyName + "." + hash + ".dll");
		if (File.Exists(cached))
		{
			KitLog.Write("  code: " + files.Length + " .cs file(s), cached build");
			return AssemblyLoadContext.Default.LoadFromAssemblyPath(cached);
		}

		var parse = new CSharpParseOptions(LanguageVersion.Latest);
		var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(GlobalUsings, parse, "GlobalUsings.cs") };
		foreach (string f in files)
		{
			trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f, Encoding.UTF8));
		}
		var compilation = CSharpCompilation.Create(assemblyName, trees, References(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
				allowUnsafe: true, nullableContextOptions: NullableContextOptions.Disable));

		using var ms = new MemoryStream();
		var result = compilation.Emit(ms);
		if (!result.Success)
		{
			IEnumerable<string> errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20)
				.Select(d =>
				{
					FileLinePositionSpan pos = d.Location.GetLineSpan();
					return $"    {Path.GetFileName(pos.Path)}({pos.StartLinePosition.Line + 1},{pos.StartLinePosition.Character + 1}): {d.Id} {d.GetMessage()}";
				});
			throw new CompileException("compile errors in code\\*.cs:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
		}
		Directory.CreateDirectory(cacheDir);
		foreach (string old in Directory.GetFiles(cacheDir, assemblyName + ".*.dll"))
		{
			TryDelete(old);
		}
		File.WriteAllBytes(cached, ms.ToArray());
		KitLog.Write("  code: compiled " + files.Length + " .cs file(s)");
		return AssemblyLoadContext.Default.LoadFromAssemblyPath(cached);
	}

	// Everything the game can see (framework + game DLLs), plus Harmony, ModKit and every DLL mod already loaded.
	private static IEnumerable<MetadataReference> References()
	{
		var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
		{
			foreach (string p in tpa.Split(Path.PathSeparator))
			{
				paths.Add(p);
			}
		}
		_ = typeof(HarmonyLib.Harmony).Assembly;
		foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (!a.IsDynamic && !string.IsNullOrEmpty(a.Location))
			{
				paths.Add(a.Location);
			}
		}
		// Keep only one file per assembly name (TPA and loaded copies can overlap).
		return paths.Where(File.Exists).GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
			.Select(g => MetadataReference.CreateFromFile(g.First()));
	}

	private static string Hash(string[] files)
	{
		using var sha = SHA256.Create();
		var sb = new StringBuilder(Loader.KitVersion.ToString()).Append(GlobalUsings);
		foreach (string f in files)
		{
			sb.Append('|').Append(Path.GetFileName(f)).Append('|').Append(File.ReadAllText(f));
		}
		// Rebuild when the game updates too.
		sb.Append(typeof(DungeonMans.dmGame).Assembly.ManifestModule.ModuleVersionId);
		return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())))[..12];
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch
		{
		}
	}
}
