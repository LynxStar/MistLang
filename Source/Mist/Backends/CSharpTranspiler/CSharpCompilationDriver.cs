using System.Collections.Immutable;
using System.Reflection;
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
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

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
        })
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path))
                refs.Add(RoslynApi.MetadataReference.CreateFromFile(path));
        }

        // mscorlib / System.Private.CoreLib
        refs.Add(RoslynApi.MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        // System.Runtime.Extensions (Convert, Math, etc.)
        var sre = Assembly.Load("System.Runtime.Extensions");
        if (sre is not null)
            refs.Add(RoslynApi.MetadataReference.CreateFromFile(sre.Location));

        return [.. refs];
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
        var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly);
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
