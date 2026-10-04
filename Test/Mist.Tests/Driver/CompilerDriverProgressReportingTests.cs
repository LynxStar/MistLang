using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

/// <summary>
/// Integration tests verifying that <see cref="CompilerDriver"/> calls the
/// <see cref="ICompilationProgressReporter"/> with correct stage data for the
/// stages it runs itself: loading, lexing and parsing, and that a project with no
/// dependencies skips Stage 1.5 without delivering anything.
/// </summary>
[Trait("Tier", "4")]
public sealed class CompilerDriverProgressReportingTests : IDisposable
{
    private readonly string scratchDir;

    public CompilerDriverProgressReportingTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-progress-" + Guid.NewGuid().ToString("N"));
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

    private static CompilerDriver CreateDriver(RecordingProgressReporter reporter)
    {
        return new CompilerDriver(new MistLexer(), new MistParser(), reporter);
    }

    private CompilerOptions CreateOptionsForSource(string source, string fileName = "Program.mist")
    {
        var userPath = Path.Combine(scratchDir, fileName);
        File.WriteAllText(userPath, source);

        var outputDir = Path.Combine(scratchDir, "output");
        var binDir = Path.Combine(scratchDir, "bin");
        _ = Directory.CreateDirectory(outputDir);
        _ = Directory.CreateDirectory(binDir);

        return new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(userPath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: binDir,
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject");
    }

    private const string ValidProgram = """
        namespace TestNs;
        public class Foo {
            public static void Main() { }
        }
        """;

    [Fact]
    public void Stage1_ReportsCorrectFileCounts()
    {
        var reporter = new RecordingProgressReporter();
        var driver = CreateDriver(reporter);
        var options = CreateOptionsForSource(ValidProgram);

        _ = driver.Compile(options);

        var details = reporter.DetailsOf("1");

        // Should have 3 detail lines: stdlib, referenced, user
        Assert.Equal(3, details.Count);

        // Stdlib count > 0 (the test project copies stdlib files to output)
        Assert.Matches(@"^\d+ stdlib files$", details[0]);
        var stdlibCount = int.Parse(details[0].Split(' ')[0]);
        Assert.True(stdlibCount > 0, "Expected stdlib file count > 0");

        // Referenced project files == 0 (no project references in this test)
        Assert.Equal("0 referenced project files", details[1]);

        // User file count == 1
        Assert.Equal("1 user files", details[2]);
    }

    [Fact]
    public void Stage2_ReportsTokenCount()
    {
        var reporter = new RecordingProgressReporter();
        var driver = CreateDriver(reporter);
        var options = CreateOptionsForSource(ValidProgram);

        _ = driver.Compile(options);

        var details = reporter.DetailsOf("2");

        _ = Assert.Single(details);
        Assert.Matches(@"^\d+ tokens$", details[0]);
        var tokenCount = int.Parse(details[0].Split(' ')[0]);
        Assert.True(tokenCount > 0, "Expected token count > 0");
    }

    [Fact]
    public void Stage3_ReportsNodeCount()
    {
        var reporter = new RecordingProgressReporter();
        var driver = CreateDriver(reporter);
        var options = CreateOptionsForSource(ValidProgram);

        _ = driver.Compile(options);

        var details = reporter.DetailsOf("3");

        _ = Assert.Single(details);
        Assert.Matches(@"^\d+ AST nodes$", details[0]);
        var nodeCount = int.Parse(details[0].Split(' ')[0]);
        Assert.True(nodeCount > 0, "Expected AST node count > 0");
    }

    [Fact]
    public void NoDependencies_SkipStage1_5_AndDeliverNothing()
    {
        var reporter = new RecordingProgressReporter();
        var listener = new CountingListener();
        var driver = new CompilerDriver(new MistLexer(), new MistParser(), reporter, listener);
        var options = CreateOptionsForSource(ValidProgram);

        var result = driver.Compile(options);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(0, listener.Received);
        Assert.Equal(["1", "2", "3", "4", "5", "6"], reporter.Events.Where(e => e.Kind == "start").Select(e => e.StageId));
    }

    private sealed class CountingListener : IResolutionDiagnosticListener
    {
        public int Received { get; private set; }

        public void Receive(Mist.Common.Diagnostics.Diagnostic diagnostic)
        {
            Received++;
        }
    }
}