using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Driver;

/// <summary>
/// Integration tests verifying that <see cref="CompilerDriver"/> calls the
/// <see cref="ICompilationProgressReporter"/> with correct stage data at each
/// pipeline boundary.
/// </summary>
public sealed class CompilerDriverProgressReportingTests : IDisposable
{
    private readonly string scratchDir;

    public CompilerDriverProgressReportingTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Spy implementation of <see cref="ICompilationProgressReporter"/> that
    /// records all calls for later assertion.
    /// </summary>
    private sealed class SpyProgressReporter : ICompilationProgressReporter
    {
        public List<(string StageId, string Title)> StageStarts { get; } = new();
        public List<(string StageId, TimeSpan Elapsed, IReadOnlyList<string> Details)> StageCompletes { get; } = new();
        public (bool Succeeded, int ErrorCount, int WarningCount, TimeSpan TotalElapsed)? Summary { get; private set; }

        public void ReportStageStart(string stageId, string title) =>
            StageStarts.Add((stageId, title));

        public void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details) =>
            StageCompletes.Add((stageId, elapsed, details));

        public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed) =>
            Summary = (succeeded, errorCount, warningCount, totalElapsed);
    }

    private static CompilerDriver CreateDriver(SpyProgressReporter spy)
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()),
            spy);
    }

    private CompilerOptions CreateOptionsForSource(string source, string fileName = "Program.mist")
    {
        var userPath = Path.Combine(scratchDir, fileName);
        File.WriteAllText(userPath, source);

        var outputDir = Path.Combine(scratchDir, "output");
        var binDir = Path.Combine(scratchDir, "bin");
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(binDir);

        return new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(userPath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: binDir,
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);
    }

    private const string ValidProgram = """
        namespace TestNs;
        public class Foo {
            public static void Main() { }
        }
        """;

    [Fact]
    public void SuccessfulBuild_ReportsAllSixStages()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptionsForSource(ValidProgram);

        driver.Compile(options);

        // Stages 1–6 should all be started and completed
        Assert.Equal(6, spy.StageStarts.Count);
        Assert.Equal(6, spy.StageCompletes.Count);

        for(var i = 0; i < 6; i++)
        {
            Assert.Equal((i + 1).ToString(), spy.StageStarts[i].StageId);
            Assert.Equal((i + 1).ToString(), spy.StageCompletes[i].StageId);
        }
    }

    [Fact]
    public void Stage1_ReportsCorrectFileCounts()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptionsForSource(ValidProgram);

        driver.Compile(options);

        var stage1 = spy.StageCompletes.First(s => s.StageId == "1");

        // Should have 3 detail lines: stdlib, referenced, user
        Assert.Equal(3, stage1.Details.Count);

        // Stdlib count > 0 (the test project copies stdlib files to output)
        Assert.Matches(@"^\d+ stdlib files$", stage1.Details[0]);
        var stdlibCount = int.Parse(stage1.Details[0].Split(' ')[0]);
        Assert.True(stdlibCount > 0, "Expected stdlib file count > 0");

        // Referenced project files == 0 (no project references in this test)
        Assert.Equal("0 referenced project files", stage1.Details[1]);

        // User file count == 1
        Assert.Equal("1 user files", stage1.Details[2]);
    }

    [Fact]
    public void Stage2_ReportsTokenCount()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptionsForSource(ValidProgram);

        driver.Compile(options);

        var stage2 = spy.StageCompletes.First(s => s.StageId == "2");

        Assert.Single(stage2.Details);
        Assert.Matches(@"^\d+ tokens$", stage2.Details[0]);
        var tokenCount = int.Parse(stage2.Details[0].Split(' ')[0]);
        Assert.True(tokenCount > 0, "Expected token count > 0");
    }

    [Fact]
    public void Stage3_ReportsNodeCount()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptionsForSource(ValidProgram);

        driver.Compile(options);

        var stage3 = spy.StageCompletes.First(s => s.StageId == "3");

        Assert.Single(stage3.Details);
        Assert.Matches(@"^\d+ AST nodes$", stage3.Details[0]);
        var nodeCount = int.Parse(stage3.Details[0].Split(' ')[0]);
        Assert.True(nodeCount > 0, "Expected AST node count > 0");
    }

    [Fact]
    public void Stage4_ReportsConstructCount()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptionsForSource(ValidProgram);

        driver.Compile(options);

        var stage4 = spy.StageCompletes.First(s => s.StageId == "4");

        Assert.Single(stage4.Details);
        Assert.Matches(@"^\d+ top-level constructs$", stage4.Details[0]);
        var constructCount = int.Parse(stage4.Details[0].Split(' ')[0]);
        // At least 1 user-code top-level construct (class Foo)
        Assert.True(constructCount >= 1, "Expected at least 1 top-level construct");
    }

    [Fact]
    public void Stage5_ReportsAnalyzerTimings()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptionsForSource(ValidProgram);

        driver.Compile(options);

        var stage5 = spy.StageCompletes.First(s => s.StageId == "5");

        // At least one timing entry (TypeCheckerAnalyzer)
        Assert.True(stage5.Details.Count >= 1, "Expected at least one analyzer timing entry");
        // Each entry should contain the analyzer ID and a time
        foreach(var detail in stage5.Details)
        {
            Assert.Matches(@"^\S+\s+\d+\.\d{2}s$", detail);
        }
    }

    [Fact]
    public void Stage6_ReportsEmitMetrics()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptionsForSource(ValidProgram);

        driver.Compile(options);

        var stage6 = spy.StageCompletes.First(s => s.StageId == "6");

        // First detail: emitted file count
        Assert.True(stage6.Details.Count >= 1, "Expected at least one emit detail");
        Assert.Matches(@"^\d+ \.cs files written$", stage6.Details[0]);
        var emittedCount = int.Parse(stage6.Details[0].Split(' ')[0]);
        Assert.True(emittedCount > 0, "Expected emitted file count > 0");

        // Second detail (if present): artifact path should be non-null/non-empty
        if(stage6.Details.Count > 1)
        {
            Assert.False(string.IsNullOrWhiteSpace(stage6.Details[1]),
                "Expected artifact path to be non-empty");
        }
    }

    [Fact]
    public void SuccessfulBuild_ReportsSummaryWithSucceeded()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptionsForSource(ValidProgram);

        var result = driver.Compile(options);

        // The driver itself does not call ReportBuildSummary — that's the
        // CLI's responsibility. Verify the build succeeded via the result.
        Assert.True(result.Succeeded, "Expected compilation to succeed");
        Assert.Empty(result.Diagnostics.Where(d =>
            d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error));
    }

    [Fact]
    public void SemanticModelFailure_SkipsAnalysisAndEmitStages()
    {
        // A program that references an undefined type causes a semantic error.
        // The semantic model builder will produce errors, causing the driver
        // to skip stages 5 (Analysis) and 6 (Emit).
        const string invalidProgram = """
            class Foo {
                public Undefined bar;
            }
            """;

        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptionsForSource(invalidProgram);

        var result = driver.Compile(options);

        // Stages 1–4 should always be reported
        Assert.Contains(spy.StageStarts, s => s.StageId == "1");
        Assert.Contains(spy.StageStarts, s => s.StageId == "2");
        Assert.Contains(spy.StageStarts, s => s.StageId == "3");
        Assert.Contains(spy.StageStarts, s => s.StageId == "4");

        Assert.Contains(spy.StageCompletes, s => s.StageId == "1");
        Assert.Contains(spy.StageCompletes, s => s.StageId == "2");
        Assert.Contains(spy.StageCompletes, s => s.StageId == "3");
        Assert.Contains(spy.StageCompletes, s => s.StageId == "4");

        // The build should have failed
        Assert.False(result.Succeeded, "Expected compilation to fail");

        // Stages 5 and 6 should NOT be started (skipped due to semantic errors)
        Assert.DoesNotContain(spy.StageStarts, s => s.StageId == "5");
        Assert.DoesNotContain(spy.StageStarts, s => s.StageId == "6");
        Assert.DoesNotContain(spy.StageCompletes, s => s.StageId == "5");
        Assert.DoesNotContain(spy.StageCompletes, s => s.StageId == "6");
    }
}