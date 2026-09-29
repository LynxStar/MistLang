using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

/// <summary>
/// The driver-to-pipeline seam on the experimental route: the driver built without classic services
/// hands its reporter to the pipeline it dispatches to, so one compile reports every stage once, in
/// order, and leaves the build summary to its caller.
/// </summary>
[Trait("Tier", "4")]
public sealed class ExperimentalPipelineProgressSeamTests : IDisposable
{
    private readonly string scratchDir = Path.Combine(Path.GetTempPath(), "mist-v2-progress-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingProgressReporter reporter = new();

    public ExperimentalPipelineProgressSeamTests()
    {
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

    [Fact]
    public void ACompileReportsEveryStageOnceInOrderAndNoSummary()
    {
        var result = Compile("""
            using Standard;

            namespace Probe;

            public class Program
            {
                public static void Main()
                {
                }
            }
            """);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}")));
        Assert.Equal(
            ["start 1", "complete 1", "start 2", "complete 2", "start 3", "complete 3", "start 4", "complete 4", "start 5", "complete 5", "start 6", "complete 6"],
            reporter.Sequence);
        Assert.Equal(0, reporter.Summaries);
        Assert.Equal(["1 top-level constructs"], reporter.DetailsOf("4"));
        Assert.Equal(result.PrimaryOutputArtifactPath, reporter.DetailsOf("6")[^1]);
    }

    [Fact]
    public void AParseFailureReportsOnlyTheStagesBeforeThePipeline()
    {
        var result = Compile("""
            namespace Probe;

            public class Program
            {
            """);

        Assert.False(result.Succeeded);
        Assert.Equal(["start 1", "complete 1", "start 2", "complete 2", "start 3", "complete 3"], reporter.Sequence);
    }

    private CompilerResult Compile(string source)
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, source);

        var driver = new CompilerDriver(new MistLexer(), new MistParser(), reporter);

        return driver.Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: [sourcePath],
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            EnabledAnalyzerIds: [],
            Pipeline: PipelineMode.Experimental));
    }
}