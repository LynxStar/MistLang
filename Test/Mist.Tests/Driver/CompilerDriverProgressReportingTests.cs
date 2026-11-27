using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

[Trait("Tier", "4")]
public sealed class CompilerDriverProgressReportingTests
{
    private static readonly Lazy<ProgressCapture> Progress = new(Capture);

    private const string ValidProgram = """
        namespace TestNs;
        public class Foo {
            public static void Main() { }
        }
        """;

    [Fact]
    public void Stage1_ReportsCorrectFileCounts()
    {
        var details = Progress.Value.Files;

        Assert.Equal(3, details.Length);

        // The test project copies stdlib files to output.
        Assert.Matches(@"^\d+ stdlib files$", details[0]);
        var stdlibCount = int.Parse(details[0].Split(' ')[0]);
        Assert.True(stdlibCount > 0, "Expected stdlib file count > 0");

        Assert.Equal("0 referenced project files", details[1]);

        Assert.Equal("1 user files", details[2]);
    }

    [Fact]
    public void Stage2_ReportsTokenCount()
    {
        var details = Progress.Value.Tokens;

        _ = Assert.Single(details);
        Assert.Matches(@"^\d+ tokens$", details[0]);
        var tokenCount = int.Parse(details[0].Split(' ')[0]);
        Assert.True(tokenCount > 0, "Expected token count > 0");
    }

    [Fact]
    public void Stage3_ReportsNodeCount()
    {
        var details = Progress.Value.Nodes;

        _ = Assert.Single(details);
        Assert.Matches(@"^\d+ AST nodes$", details[0]);
        var nodeCount = int.Parse(details[0].Split(' ')[0]);
        Assert.True(nodeCount > 0, "Expected AST node count > 0");
    }

    [Fact]
    public void NoDependencies_SkipStage1_5_AndDeliverNothing()
    {
        Assert.Equal(0, Progress.Value.ResolutionDiagnostics);
        Assert.Equal<string>(["1", "2", "3", "4", "5", "6"], Progress.Value.StartedStages);
    }

    private static ProgressCapture Capture()
    {
        var scratch = Directory.CreateTempSubdirectory("mist-progress-");
        try
        {
            var userPath = Path.Combine(scratch.FullName, "Program.mist");
            File.WriteAllText(userPath, ValidProgram);
            var options = new CompilerOptions(
                ProjectFolder: scratch.FullName,
                SourcePaths: [userPath],
                Backend: new BackendId("csharp"),
                OutputDirectory: scratch.CreateSubdirectory("output").FullName,
                BinDirectory: scratch.CreateSubdirectory("bin").FullName,
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject");

            var reporter = new RecordingProgressReporter();
            var listener = new CountingListener();
            var driver = new CompilerDriver(new MistLexer(), new MistParser(), reporter, listener, ambientTypes: _ => StubAmbientTypeSource.Instance);
            var result = driver.Compile(options);
            Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));

            return new ProgressCapture([.. reporter.DetailsOf("1")], [.. reporter.DetailsOf("2")], [.. reporter.DetailsOf("3")],
                [.. reporter.Events.Where(e => e.Kind == "start").Select(e => e.StageId)], listener.Received);
        }
        finally
        {
            try { scratch.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed record ProgressCapture(
        ImmutableArray<string> Files,
        ImmutableArray<string> Tokens,
        ImmutableArray<string> Nodes,
        ImmutableArray<string> StartedStages,
        int ResolutionDiagnostics);

    private sealed class CountingListener : IResolutionDiagnosticListener
    {
        public int Received { get; private set; }

        public void Receive(Mist.Common.Diagnostics.Diagnostic diagnostic)
        {
            Received++;
        }
    }
}