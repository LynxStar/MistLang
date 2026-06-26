using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using MistDiagnosticSeverity = Mist.Common.Diagnostics.DiagnosticSeverity;
using RoslynApi = Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Compiles emitted C# source files into an executable assembly using the
/// Roslyn API in-process, avoiding the overhead of spawning a <c>dotnet build</c>
/// subprocess per compilation.
/// </summary>
public sealed class CSharpCompilationDriver
{
    // Core runtime references needed for every compilation.
    // Resolved once and reused across all compilations.
    // Internal so that in-process Roslyn verification tests can reuse the same reference set.
    internal static readonly ImmutableArray<RoslynApi.MetadataReference> CoreReferences = BuildCoreReferences();

    private static ImmutableArray<RoslynApi.MetadataReference> BuildCoreReferences()
    {
        // Locate the runtime directory so we can reference the core assemblies.
        // In single-file publish mode, Assembly.Location returns "" so we fall
        // back to the shared framework directory discovered via RuntimeEnvironment.
        var runtimeDir = GetRuntimeDirectory();
        var refPackDir = TryResolveRefPackDir();

        var refs = new List<RoslynApi.MetadataReference>();

        // When a reference-assembly pack is available, reference ALL assemblies
        // from it. This ensures that transitive framework dependencies (e.g.,
        // System.Data.Common referenced by EF Core NuGet packages) are available
        // to the Roslyn compilation without maintaining a manual list.
        if(refPackDir is not null)
        {
            foreach(var dll in Directory.GetFiles(refPackDir, "*.dll"))
            {
                refs.Add(RoslynApi.MetadataReference.CreateFromFile(dll));
            }

            return [.. refs];
        }

        // Fallback: no ref pack installed — use the minimal set from the runtime dir.
        foreach(var name in new[]
        {
            "System.Runtime.dll",
            "System.Console.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Text.RegularExpressions.dll",
            "System.Threading.dll",
            "netstandard.dll",
            "System.Private.CoreLib.dll",
            "System.Runtime.Extensions.dll",
        })
        {
            var path = Path.Combine(runtimeDir, name);
            if(File.Exists(path))
                refs.Add(RoslynApi.MetadataReference.CreateFromFile(path));
        }

        // Dynamic-dispatch supporting libraries (Task 31 revised,
        // Req 15.1, 15.2). `Microsoft.CSharp` is the primary — it
        // carries the DLR binder surface that Roslyn compiles `dynamic`
        // dispatch against. `System.Linq.Expressions` and
        // `System.Dynamic.Runtime` are transitively required by the
        // DLR's lowering.
        foreach(var name in new[]
        {
            "Microsoft.CSharp.dll",
            "System.Linq.Expressions.dll",
            "System.Dynamic.Runtime.dll",
        })
        {
            var path = Path.Combine(runtimeDir, name);
            if(File.Exists(path))
                refs.Add(RoslynApi.MetadataReference.CreateFromFile(path));
        }

        return [.. refs];
    }

    /// <summary>
    /// Attempts to locate the .NET SDK's reference-assembly directory
    /// for the currently running runtime's major.minor version. Returns
    /// the path to the ref DLLs (e.g.,
    /// <c>/usr/local/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.7/ref/net10.0</c>)
    /// when a ref pack is installed, or <c>null</c> when only the
    /// runtime is installed (in which case the caller uses the
    /// shared-framework implementation DLLs).
    /// </summary>
    private static string? TryResolveRefPackDir()
    {
        var dotnetRoot = GetDotnetRoot();
        if(dotnetRoot is null) return null;

        var refPackRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if(!Directory.Exists(refPackRoot)) return null;

        // Pick the highest version directory matching the running
        // runtime's major.minor, then fall back to the highest overall.
        var runtimeMajorMinor = $"{Environment.Version.Major}.{Environment.Version.Minor}.";
        var versionDirs = Directory.GetDirectories(refPackRoot)
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .ToArray();

        var matched = versionDirs.FirstOrDefault(
            d => Path.GetFileName(d).StartsWith(runtimeMajorMinor, StringComparison.Ordinal));
        var selected = matched ?? versionDirs.FirstOrDefault();
        if(selected is null) return null;

        // Pick the highest tfm directory under `ref/`.
        var refDir = Path.Combine(selected, "ref");
        if(!Directory.Exists(refDir)) return null;

        var tfmDirs = Directory.GetDirectories(refDir)
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .ToArray();

        return tfmDirs.FirstOrDefault();
    }

    /// <summary>
    /// Resolves the .NET runtime directory containing the core framework assemblies.
    /// Handles both normal execution (Assembly.Location available) and single-file
    /// publish mode (Assembly.Location is empty, must discover shared framework path).
    /// </summary>
    private static string GetRuntimeDirectory()
    {
        // Try Assembly.Location first (works in non-single-file scenarios).
        var assemblyLocation = typeof(object).Assembly.Location;
        if(!string.IsNullOrEmpty(assemblyLocation))
        {
            var dir = Path.GetDirectoryName(assemblyLocation);
            if(!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return dir;
        }

        // Single-file publish: discover the shared framework directory.
        // The runtime is at: <dotnet-root>/shared/Microsoft.NETCore.App/<version>/
        var dotnetRoot = GetDotnetRoot();
        if(dotnetRoot is not null)
        {
            var sharedDir = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
            if(Directory.Exists(sharedDir))
            {
                // Pick the highest version directory that exists.
                var versionDirs = Directory.GetDirectories(sharedDir)
                    .OrderByDescending(d => Path.GetFileName(d))
                    .ToArray();

                // Prefer the version matching our runtime.
                var runtimeVersion = Environment.Version.ToString();
                var exactMatch = versionDirs.FirstOrDefault(d =>
                    Path.GetFileName(d).StartsWith($"{Environment.Version.Major}.{Environment.Version.Minor}."));

                var selectedDir = exactMatch ?? versionDirs.FirstOrDefault();
                if(selectedDir is not null && Directory.Exists(selectedDir))
                    return selectedDir;
            }
        }

        // Last resort: use RuntimeEnvironment.GetRuntimeDirectory() which
        // should work in all scenarios but may not be available in trimmed apps.
        return System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
    }

    /// <summary>
    /// Discovers the .NET root directory (e.g., /usr/local/share/dotnet).
    /// </summary>
    private static string? GetDotnetRoot()
    {
        // Check DOTNET_ROOT environment variable first.
        var envRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if(!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
            return envRoot;

        // Common locations per platform.
        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"),
            }
            : new[]
            {
                "/usr/local/share/dotnet",
                "/usr/share/dotnet",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"),
            };

        foreach(var candidate in candidates)
        {
            if(Directory.Exists(candidate) &&
                Directory.Exists(Path.Combine(candidate, "shared", "Microsoft.NETCore.App")))
                return candidate;
        }

        return null;
    }

    public static BackendResult Compile(
        string projectDirectory,
        string binDirectory,
        string projectName,
        DiagnosticBag diagnostics,
        int emittedFileCount = 0,
        string? mainTypeName = null,
        ImmutableArray<string> additionalReferencePaths = default)
    {
        if(!Directory.Exists(binDirectory))
            Directory.CreateDirectory(binDirectory);

        // Collect all emitted .cs files
        var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
        if(csFiles.Length == 0)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_BUILD_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Backend,
                "No C# source files found to compile.",
                Location: default,
                CompilationUnitPath: "<unknown>",
                AnalyzerName: null));
            return new BackendResult(false, null, diagnostics.ToImmutableArray(), EmittedFileCount: emittedFileCount);
        }

        // Parse each .cs file into a SyntaxTree
        var syntaxTrees = csFiles
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                CSharpParseOptions.Default.WithLanguageVersion(RoslynApi.CSharp.LanguageVersion.Preview),
                path: path))
            .ToList();

        // Build the Roslyn compilation
        var compilationOptions = new CSharpCompilationOptions(
            RoslynApi.OutputKind.ConsoleApplication,
            optimizationLevel: RoslynApi.OptimizationLevel.Debug,
            nullableContextOptions: RoslynApi.NullableContextOptions.Enable,
            deterministic: true,
            mainTypeName: mainTypeName);

        var compilation = CSharpCompilation.Create(
            assemblyName: projectName,
            syntaxTrees: syntaxTrees,
            references: additionalReferencePaths.IsDefaultOrEmpty
                ? CoreReferences
                : CoreReferences.AddRange(
                    additionalReferencePaths
                        .Where(File.Exists)
                        .Select(p => (RoslynApi.MetadataReference)RoslynApi.MetadataReference.CreateFromFile(p))),
            options: compilationOptions);

        // Emit to the bin directory
        var outputPath = Path.Combine(binDirectory, $"{projectName}.dll");
        var runtimeConfigPath = Path.Combine(binDirectory, $"{projectName}.runtimeconfig.json");

        EmitResult result;
        using(var peStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            result = compilation.Emit(peStream);
        }

        // Map Roslyn diagnostics back to Mist diagnostics
        foreach(var roslynDiag in result.Diagnostics)
        {
            if(roslynDiag.Severity == RoslynApi.DiagnosticSeverity.Error)
            {
                var location = roslynDiag.Location.IsInSource
                    ? roslynDiag.Location.SourceTree?.FilePath
                    : null;

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_CS_ERROR"),
                    MistDiagnosticSeverity.Error,
                    PipelineStage.Backend,
                    roslynDiag.GetMessage(),
                    Location: default,
                    CompilationUnitPath: location ?? "<unknown>",
                    AnalyzerName: null));
            }
        }

        if(!result.Success)
        {
            if(File.Exists(outputPath))
                File.Delete(outputPath);
            return new BackendResult(false, null, diagnostics.ToImmutableArray(), EmittedFileCount: emittedFileCount);
        }

        // Write a .runtimeconfig.json so `dotnet <dll>` knows which runtime to use
        WriteRuntimeConfig(runtimeConfigPath, additionalReferencePaths);

        // Copy package assembly DLLs to the bin directory so they're found at runtime.
        if(!additionalReferencePaths.IsDefaultOrEmpty)
        {
            foreach(var refPath in additionalReferencePaths)
            {
                if(File.Exists(refPath))
                {
                    var destPath = Path.Combine(binDirectory, Path.GetFileName(refPath));
                    if(!File.Exists(destPath))
                    {
                        try { File.Copy(refPath, destPath); } catch { /* best effort */ }
                    }
                }
            }
        }

        return new BackendResult(true, outputPath, diagnostics.ToImmutableArray(), EmittedFileCount: emittedFileCount);
    }

    private static void WriteRuntimeConfig(string path, ImmutableArray<string> additionalReferencePaths = default)
    {
        var tfm = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        // Extract version like "10.0.3" from ".NET 10.0.3"
        var version = tfm.Replace(".NET ", "").Trim();
        // Normalise to major.minor (e.g. "10.0")
        var parts = version.Split('.');
        var majorMinor = parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;

        // Check if additional reference paths include ASP.NET Core assemblies
        // (indicating that Microsoft.AspNetCore.App framework should be included).
        var includeAspNetCore = !additionalReferencePaths.IsDefaultOrEmpty &&
            additionalReferencePaths.Any(p =>
                p.Contains("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase));

        string json;
        if(includeAspNetCore)
        {
            json = $$"""
                {
                  "runtimeOptions": {
                    "tfm": "net{{majorMinor}}",
                    "frameworks": [
                      {
                        "name": "Microsoft.NETCore.App",
                        "version": "{{version}}"
                      },
                      {
                        "name": "Microsoft.AspNetCore.App",
                        "version": "{{version}}"
                      }
                    ]
                  }
                }
                """;
        }
        else
        {
            json = $$"""
                {
                  "runtimeOptions": {
                    "tfm": "net{{majorMinor}}",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "{{version}}"
                    }
                  }
                }
                """;
        }
        File.WriteAllText(path, json);
    }
}