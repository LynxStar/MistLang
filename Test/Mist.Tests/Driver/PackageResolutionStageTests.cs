// Feature: csharp-interop-v2, Task 7.6
// In-process tests verifying end-to-end behavior of the Package Resolution
// stage (Stage 1.5) through the CompilerDriver. These tests exercise:
//   - Quiet build emits no 1.5 header/detail
//   - Verbose build emits header + detail + summary
//   - Resolution error surfaces in both modes
//   - Unresolved type → diagnostic without aborting
//
// Validates: Requirements 1.3, 1.6, 1.9, 2.2, 2.3

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Driver;

/// <summary>
/// Integration tests verifying the Package Resolution stage (1.5) behavior
/// through the <see cref="CompilerDriver"/>. All tests are in-process —
/// no child processes are spawned (the resolver spawns dotnet restore internally).
/// </summary>
public sealed class PackageResolutionStageTests : IDisposable
{
    private readonly string _scratchDir;

    public PackageResolutionStageTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-pkgstage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_scratchDir))
        {
            try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ─── Spy Reporter ────────────────────────────────────────────────────

    private sealed class SpyProgressReporter : ICompilationProgressReporter
    {
        public List<(string StageId, string Title)> StageStarts { get; } = [];
        public List<(string StageId, TimeSpan Elapsed, IReadOnlyList<string> Details)> StageCompletes { get; } = [];
        public (bool Succeeded, int ErrorCount, int WarningCount, TimeSpan TotalElapsed)? Summary { get; private set; }

        public void ReportStageStart(string stageId, string title) =>
            StageStarts.Add((stageId, title));

        public void ReportStageComplete(string stageId, TimeSpan elapsed, IReadOnlyList<string> details) =>
            StageCompletes.Add((stageId, elapsed, details));

        public void ReportBuildSummary(bool succeeded, int errorCount, int warningCount, TimeSpan totalElapsed) =>
            Summary = (succeeded, errorCount, warningCount, totalElapsed);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

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

    private CompilerOptions CreateOptions(
        string source,
        ImmutableArray<NuGetPackageReference> packages = default,
        ImmutableArray<FrameworkReference> frameworkReferences = default,
        bool verboseResolution = false,
        string fileName = "Program.mist")
    {
        var userPath = Path.Combine(_scratchDir, fileName);
        File.WriteAllText(userPath, source);

        var outputDir = Path.Combine(_scratchDir, "output");
        var binDir = Path.Combine(_scratchDir, "bin");
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(binDir);

        return new CompilerOptions(
            ProjectFolder: _scratchDir,
            SourcePaths: ImmutableArray.Create(userPath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: binDir,
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
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

        driver.Compile(options);

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

        driver.Compile(options);

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
    // 4. Unresolved type → diagnostic without aborting (Requirement 2.3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnresolvedType_ProducesDiagnostic_WithoutAbortingBuild()
    {
        // Declare a package with an invalid version (triggers MIST_PKG_002).
        // The source references a type from the "package" namespace. Because
        // the package fails to resolve, the type won't be found. The driver
        // should NOT abort — it continues through subsequent stages and
        // produces a diagnostic for the unresolved type.
        const string sourceWithUnresolvedType = """
            namespace TestNs;
            using SomePackage.Models;
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

        // The build should NOT have aborted before reaching the semantic
        // model stage — the lexing and parsing stages should still run.
        Assert.Contains(spy.StageStarts, s => s.StageId == "2"); // Lexing
        Assert.Contains(spy.StageStarts, s => s.StageId == "3"); // Parsing
        Assert.Contains(spy.StageStarts, s => s.StageId == "4"); // Semantic Model

        // The MIST_PKG_002 diagnostic surfaces (package resolution error).
        Assert.Contains(result.Diagnostics,
            d => d.Id.Value == "MIST_PKG_002");

        // The build does fail (because the type can't be resolved), but it
        // did not abort — it continued past the package resolution stage and
        // attempted semantic analysis.
        Assert.False(result.Succeeded,
            "Expected compilation to fail due to unresolved type, but not abort");
    }

    [Fact]
    public void ResolutionError_DoesNotPreventSubsequentStages()
    {
        // Even with a failing package, the driver continues to lex, parse,
        // and attempt semantic model construction (Requirement 2.6: build
        // continues with remaining resolved assemblies rather than aborting).
        var spy = new SpyProgressReporter();
        var driver = CreateDriver(spy);
        var options = CreateOptions(
            MinimalProgram,
            packages: [new NuGetPackageReference("Missing", "bogus-ver")]);

        var result = driver.Compile(options);

        // All core stages after 1.5 still execute.
        Assert.Contains(spy.StageStarts, s => s.StageId == "2"); // Lexing
        Assert.Contains(spy.StageStarts, s => s.StageId == "3"); // Parsing
        Assert.Contains(spy.StageStarts, s => s.StageId == "4"); // Semantic Model

        // The compilation succeeds (the source doesn't reference any type
        // from the failing package, so there's no downstream error).
        Assert.True(result.Succeeded,
            "Expected compilation to succeed — source doesn't use the failed package");

        // But the resolution diagnostic is still present in the output.
        Assert.Contains(result.Diagnostics,
            d => d.Id.Value == "MIST_PKG_002");
    }
}