using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 4, the boundary from package resolution (Stage 1.5) to the standard pipeline's Roslyn
/// compilation.
/// <para>
/// BINDING SUCCESS PROVES NOTHING ABOUT THIS SEAM. The ambient provider reads the same resolved
/// assemblies, so a package type resolves to a model member whether or not Roslyn was ever told
/// where that type lives; the compiler then reports success and hands back MIST_CS_ERROR against a
/// generated file. Only compiling the emitted C# separates the two.
/// </para>
/// </summary>
[Trait("Tier", "4")]
public sealed class ResolvedPackagesReachV2CompilationTests : IDisposable
{
    private readonly string scratchDir;

    public ResolvedPackagesReachV2CompilationTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-v2pkg-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    private const string UsesAPackageType = """
        using Standard;
        using Newtonsoft.Json;

        namespace PackageProbe;

        public class Probe
        {
            public static void Main()
            {
                int[] data = [1, 2, 3];
                var json = JsonConvert.SerializeObject(data);
                Console.WriteLine(json);
            }
        }
        """;

    [Fact]
    public void PackageTypeInEmittedCSharp_CompilesOnTheStandardPipeline()
    {
        var sourcePath = Path.Combine(scratchDir, "Probe.mist");
        File.WriteAllText(sourcePath, UsesAPackageType);

        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser());

        var result = driver.Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "PackageProbe",
            NamespaceRoot: "PackageProbe",
            Packages: [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")]));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"A declared package's assemblies did not reach the v2 Roslyn compilation:\n{string.Join("\n", errors)}");
        Assert.True(result.Succeeded);
    }
}