// Feature: csharp-interop-v2, Task 7.6
// In-process tests verifying end-to-end behavior of the Package Resolution
// stage (Stage 1.5) through the CompilerDriver. These tests exercise:
//   - Quiet build emits no 1.5 header/detail
//   - Verbose build emits header + detail + summary
//   - Resolution error surfaces in both modes
//   - Resolution error runs the build through analysis and fails it before emission
//   - Each resolution failure reaches the listener during Stage 1.5, before lexing
//
// Validates: Requirements 1.3, 1.6, 1.9, 2.3, 2.6, 3.5, 3.6, 5.7

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

/// <summary>
/// Integration tests verifying the Package Resolution stage (1.5) behavior
/// through the <see cref="CompilerDriver"/>. All tests are in-process —
/// no child processes are spawned (the resolver spawns dotnet restore internally).
/// </summary>
[Trait("Tier", "4")]
public sealed class PackageResolutionStageTests : IDisposable
{
    private readonly string scratchDir;

    public PackageResolutionStageTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-pkgstage-" + Guid.NewGuid().ToString("N"));
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

    // ─── Spy Reporter ────────────────────────────────────────────────────

    private sealed class SpyProgressReporter : ICompilationProgressReporter
    {
        public List<(string StageId, string Title)> StageStarts { get; } = [];
        public List<(string StageId, TimeSpan Elapsed, IReadOnlyList<string> Details)> StageCompletes { get; } = [];
        public (bool Succeeded, int ErrorCount, int WarningCount, TimeSpan TotalElapsed)? Summary { get; private set; }

        public void ReportStageStart(string stageId, string title)
        {
            StageStarts.Add((stageId, title));
        }

        public void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details)
        {
            StageCompletes.Add((stageId, elapsed, details));
        }

        public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed)
        {
            Summary = (succeeded, errorCount, warningCount, totalElapsed);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CompilerDriver CreateDriver(SpyProgressReporter spy)
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            spy,
            ambientTypes: _ => StubAmbientTypeSource.Instance);
    }

    private CompilerOptions CreateOptions(
        string source,
        ImmutableArray<NuGetPackageReference> packages = default,
        ImmutableArray<FrameworkReference> frameworkReferences = default,
        bool verboseResolution = false,
        string fileName = "Program.mist")
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
            NamespaceRoot: "TestProject",
            Packages: packages,
            FrameworkReferences: frameworkReferences,
            VerboseResolution: verboseResolution);
    }

    private const string MinimalProgram = """
        namespace TestNs;
        public class Foo {
            public static void Main() { }
        }
        """;

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Quiet build emits no 1.5 header/detail (Requirement 1.3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void QuietBuild_WithPackages_DoesNotEmitStage1_5()
    {
        // Even when packages are declared, VerboseResolution=false (the
        // default) should suppress all reporter output for stage 1.5.
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")],
            verboseResolution: false);

        _ = driver.Compile(options);

        // Stage 1.5 should NOT appear in reporter starts or completes.
        Assert.DoesNotContain(spy.StageStarts, s => s.StageId == "1.5");
        Assert.DoesNotContain(spy.StageCompletes, s => s.StageId == "1.5");

        // No detail/title strings should mention "Package Resolution" or "1.5".
        var allTitles = spy.StageStarts.Select(s => s.Title);
        Assert.DoesNotContain(allTitles, t => t.Contains("Package Resolution"));
    }

    [Fact]
    public void QuietBuild_WithFailedPackage_DoesNotEmitStage1_5_ButDiagnosticSurfaces()
    {
        // When resolution fails and verbose is off, the stage is still silent
        // (no header/detail), but the diagnostic surfaces in the result.
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("NonExistent", "invalid-ver")],
            verboseResolution: false);

        var result = driver.Compile(options);

        // No stage 1.5 reporter output.
        Assert.DoesNotContain(spy.StageStarts, s => s.StageId == "1.5");
        Assert.DoesNotContain(spy.StageCompletes, s => s.StageId == "1.5");

        // But the MIST_PKG_002 diagnostic still surfaces (Requirement 1.9).
        Assert.Contains(result.Diagnostics,
            d => d.Id.Value == "MIST_PKG_002");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Verbose build emits header + detail + summary (Requirement 1.6)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VerboseBuild_EmitsStage1_5_HeaderDetailSummary()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")],
            verboseResolution: true);

        _ = driver.Compile(options);

        // Stage header: starts with stageId "1.5" and title "Package Resolution".
        var stageStart = spy.StageStarts.FirstOrDefault(s => s.StageId == "1.5");
        Assert.NotEqual(default, stageStart);
        Assert.Equal("Package Resolution", stageStart.Title);

        // Stage complete reported.
        var stageComplete = spy.StageCompletes.FirstOrDefault(s => s.StageId == "1.5");
        Assert.NotEqual(default, stageComplete);

        var details = stageComplete.Details;
        Assert.NotNull(details);
        Assert.True(details.Count >= 3,
            $"Expected at least 3 detail lines (restored, ms, summary), got {details.Count}");

        // Detail line should contain the package restoration info.
        Assert.Contains(details, d => d.Contains("Restored") && d.Contains("Newtonsoft.Json"));

        // Elapsed ms line (integer milliseconds).
        Assert.Contains(details, d => d.EndsWith("ms"));

        // Summary line with succeeded/failed counts.
        Assert.Contains(details, d => d.Contains("succeeded") && d.Contains("failed"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Resolution error surfaces in both modes (Requirement 1.9)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolutionError_SurfacesInQuietMode()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("BadPkg", "not-valid")],
            verboseResolution: false);

        var result = driver.Compile(options);

        // The MIST_PKG_002 diagnostic appears in the compilation result
        // regardless of verbose setting.
        var diag = result.Diagnostics.FirstOrDefault(
            d => d.Id.Value == "MIST_PKG_002");
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("BadPkg", diag.Message);
    }

    [Fact]
    public void ResolutionError_SurfacesInVerboseMode()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("BadPkg", "not-valid")],
            verboseResolution: true);

        var result = driver.Compile(options);

        // The MIST_PKG_002 diagnostic appears in the compilation result.
        var diag = result.Diagnostics.FirstOrDefault(
            d => d.Id.Value == "MIST_PKG_002");
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("BadPkg", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Resolution error does not stop binding and analysis, and fails the
    //    build before emission (Requirements 2.3, 2.6)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolutionError_RunsStagesOneToFive_AndFailsTheBuildWithoutEmitting()
    {
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("Missing", "bogus-ver")]);

        var result = driver.Compile(options);

        Assert.Equal(["1", "2", "3", "4", "5"], spy.StageStarts.Select(s => s.StageId));
        Assert.False(result.Succeeded, "The source uses nothing from the package, and the build still fails on it.");
        Assert.Null(result.PrimaryOutputArtifactPath);
        Assert.Equal(["MIST_PKG_002"], result.Diagnostics.Select(d => d.Id.Value));
    }

    [Fact]
    public void UnresolvedType_ProducesDiagnostic_WithoutAbortingBuild()
    {
        const string sourceWithUnresolvedType = """
            using SomePackage.Models;
            namespace TestNs;
            public class Foo {
                public static void Main() {
                    var x = new Widget();
                }
            }
            """;

        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptions(
            sourceWithUnresolvedType,
            packages: [new NuGetPackageReference("SomePackage", "invalid-ver")]);

        var result = driver.Compile(options);

        Assert.Equal(["1", "2", "3", "4", "5"], spy.StageStarts.Select(s => s.StageId));
        Assert.False(result.Succeeded);
        Assert.Equal(
            ["MIST_PKG_002", "MIST_UNDEFINED_NAMESPACE", "MIST_PM_UNRESOLVED_TYPE"],
            result.Diagnostics.Select(d => d.Id.Value));
        Assert.Contains("SomePackage.Models", result.Diagnostics[1].Message);
        Assert.Contains("Widget", result.Diagnostics[2].Message);
    }

    [Fact]
    public void ResolutionError_IsReportedInStageOrder_AndLeavesTheEntryGateToTheStagesAroundIt()
    {
        const string sourceThatDoesNotParse = """
            namespace TestNs;
            public class Foo {
                public static void Main() {
            """;

        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptions(
            sourceThatDoesNotParse,
            packages: [new NuGetPackageReference("Missing", "bogus-ver")]);
        options = options with { SourcePaths = [Path.Combine(scratchDir, "Absent.mist"), .. options.SourcePaths] };

        var result = driver.Compile(options);

        Assert.Equal(["1", "2", "3"], spy.StageStarts.Select(s => s.StageId));
        Assert.False(result.Succeeded);
        Assert.Equal(
            ["MIST_FILE_ERROR", "MIST_PKG_002", "MIST_PARSE_INCOMPLETE"],
            result.Diagnostics.Select(d => d.Id.Value));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Each failure reaches the listener during Stage 1.5, before Stage 2
    //    begins, and stays in the result (Requirements 1.9, 3.5, 3.6, 5.7)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolutionFailures_ReachTheListenerOneByOneBeforeLexing_AndStayInTheResult()
    {
        var timeline = new Timeline();
        var driver = CreateDriver(timeline);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("BadA", "not-valid"), new NuGetPackageReference("BadB", "also-not-valid")],
            frameworkReferences: [new FrameworkReference("Not.A.Framework")]);

        var result = driver.Compile(options);

        Assert.Equal(
            ["start 1", "complete 1", "receive MIST_PKG_002", "receive MIST_PKG_002", "receive MIST_FWREF_002", "start 2"],
            timeline.Events.Take(6));
        Assert.Contains("BadA", timeline.Received[0].Message);
        Assert.Contains("BadB", timeline.Received[1].Message);
        Assert.Contains("Not.A.Framework", timeline.Received[2].Message);
        Assert.Contains("complete 5", timeline.Events);
        Assert.False(result.Succeeded);
        Assert.Equal(3, result.Diagnostics.Length);
        for(var i = 0; i < 3; i++)
        {
            Assert.Same(timeline.Received[i], result.Diagnostics[i]);
        }
    }

    [Fact]
    public void VerboseResolution_DeliversTheFailureInsideTheStage()
    {
        var timeline = new Timeline();
        var driver = CreateDriver(timeline);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("BadPkg", "not-valid")],
            verboseResolution: true);

        _ = driver.Compile(options);

        Assert.Equal(
            ["start 1", "complete 1", "start 1.5", "receive MIST_PKG_002", "complete 1.5", "start 2"],
            timeline.Events.Take(6));
    }

    [Fact]
    public void RepeatedCompiles_EachDeliverTheirOwnFailureOnce()
    {
        var timeline = new Timeline();
        var driver = CreateDriver(timeline);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("BadPkg", "not-valid")]);

        var first = driver.Compile(options);
        var second = driver.Compile(options);

        Assert.Equal(2, timeline.Received.Count);
        Assert.Same(timeline.Received[0], Assert.Single(first.Diagnostics));
        Assert.Same(timeline.Received[1], Assert.Single(second.Diagnostics));
        Assert.Equal(2, timeline.Events.Count(e => e == "receive MIST_PKG_002"));
    }

    private static CompilerDriver CreateDriver(Timeline timeline)
    {
        return new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser(), timeline, timeline, ambientTypes: _ => StubAmbientTypeSource.Instance);
    }

    private sealed class Timeline : ICompilationProgressReporter, IResolutionDiagnosticListener
    {
        public List<string> Events { get; } = [];

        public List<Diagnostic> Received { get; } = [];

        public void ReportStageStart(string stageId, string title)
        {
            Events.Add($"start {stageId}");
        }

        public void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details)
        {
            Events.Add($"complete {stageId}");
        }

        public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed)
        {
            Events.Add("summary");
        }

        public void Receive(Diagnostic diagnostic)
        {
            Events.Add($"receive {diagnostic.Id.Value}");
            Received.Add(diagnostic);
        }
    }
}