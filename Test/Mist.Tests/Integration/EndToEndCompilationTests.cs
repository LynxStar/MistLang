using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Integration;

/// <summary>
/// End-to-end integration tests that exercise the full CompilerDriver pipeline.
/// </summary>
[Trait("Tier", "4")]
public sealed class EndToEndCompilationTests : IDisposable
{
    private readonly string outputDir;

    public EndToEndCompilationTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistE2E_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    private static CompilerDriver CreateDriver()
    {
        return new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser(), ambientTypes: _ => StubAmbientTypeSource.Instance);
    }

    private CompilerOptions MakeOptions(params string[] sourcePaths)
    {
        return new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject");
    }

    [Fact]
    public void FullPipeline_WithNonExistentFile_ProducesFileErrorDiagnostic()
    {
        var driver = CreateDriver();
        var options = MakeOptions("/nonexistent/path/test.mist");

        var result = driver.Compile(options);

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Number.Value == "MIST_FILE_ERROR");
    }
}