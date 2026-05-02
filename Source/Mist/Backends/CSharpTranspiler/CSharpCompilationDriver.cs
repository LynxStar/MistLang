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
    private static readonly ImmutableArray<RoslynApi.MetadataReference> CoreReferences = BuildCoreReferences();

    private static ImmutableArray<RoslynApi.MetadataReference> BuildCoreReferences()
    {
        // Locate the runtime directory so we can reference the core assemblies.
        // In single-file publish mode, Assembly.Location returns "" so we fall
        // back to the shared framework directory discovered via RuntimeEnvironment.
        var runtimeDir = GetRuntimeDirectory();

        var refs = new List<RoslynApi.MetadataReference>();

        // Core runtime assemblies
        foreach (var name in new[]
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
            if (File.Exists(path))
                refs.Add(RoslynApi.MetadataReference.CreateFromFile(path));
        }

        return [.. refs];
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
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var dir = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return dir;
        }

        // Single-file publish: discover the shared framework directory.
        // The runtime is at: <dotnet-root>/shared/Microsoft.NETCore.App/<version>/
        var dotnetRoot = GetDotnetRoot();
        if (dotnetRoot is not null)
        {
            var sharedDir = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
            if (Directory.Exists(sharedDir))
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
                if (selectedDir is not null && Directory.Exists(selectedDir))
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
        if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
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

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) &&
                Directory.Exists(Path.Combine(candidate, "shared", "Microsoft.NETCore.App")))
                return candidate;
        }

        return null;
    }

    public static BackendResult Compile(
        string projectDirectory,
        string binDirectory,
        string projectName,
        DiagnosticBag diagnostics)
    {
        if (!Directory.Exists(binDirectory))
            Directory.CreateDirectory(binDirectory);

        // Collect all emitted .cs files
        var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
        if (csFiles.Length == 0)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_BUILD_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Backend,
                "No C# source files found to compile.",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: null));
            return new BackendResult(false, null, diagnostics.ToImmutableArray());
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
            deterministic: true);

        var compilation = CSharpCompilation.Create(
            assemblyName: projectName,
            syntaxTrees: syntaxTrees,
            references: CoreReferences,
            options: compilationOptions);

        // Emit to the bin directory
        var outputPath = Path.Combine(binDirectory, $"{projectName}.dll");
        var runtimeConfigPath = Path.Combine(binDirectory, $"{projectName}.runtimeconfig.json");

        EmitResult result;
        using (var peStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            result = compilation.Emit(peStream);
        }

        // Map Roslyn diagnostics back to Mist diagnostics
        foreach (var roslynDiag in result.Diagnostics)
        {
            if (roslynDiag.Severity == RoslynApi.DiagnosticSeverity.Error)
            {
                var location = roslynDiag.Location.IsInSource
                    ? roslynDiag.Location.SourceTree?.FilePath
                    : null;

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_CS_ERROR"),
                    MistDiagnosticSeverity.Error,
                    PipelineStage.Backend,
                    roslynDiag.GetMessage(),
                    Location: null,
                    CompilationUnitPath: location,
                    AnalyzerName: null));
            }
        }

        if (!result.Success)
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            return new BackendResult(false, null, diagnostics.ToImmutableArray());
        }

        // Write a .runtimeconfig.json so `dotnet <dll>` knows which runtime to use
        WriteRuntimeConfig(runtimeConfigPath);

        return new BackendResult(true, outputPath, diagnostics.ToImmutableArray());
    }

    private static void WriteRuntimeConfig(string path)
    {
        var tfm = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        // Extract version like "10.0.3" from ".NET 10.0.3"
        var version = tfm.Replace(".NET ", "").Trim();
        // Normalise to major.minor (e.g. "10.0")
        var parts = version.Split('.');
        var majorMinor = parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;

        var json = $$"""
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
        File.WriteAllText(path, json);
    }
}
