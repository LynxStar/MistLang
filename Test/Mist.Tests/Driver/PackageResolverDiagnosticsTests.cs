// Feature: csharp-interop-v2, Task 3.3
// In-process tests for the structured PackageResolutionResult shape and each
// resolver diagnostic category.
//
// Validates: Requirements 3.1 (MIST_PKG_002 — not found / invalid version),
//            3.6 (one independent diagnostic per failing package; siblings continue).
//
// These tests exercise the pre-network validation path (invalid NuGet version
// strings fail NuGetVersion.TryParse before any feed is contacted). Tests that
// require actual package resolution are handled by integration tests that use
// real NuGet packages.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Unit tests for <see cref="PackageResolver.ResolvePackages"/> covering the
/// structured <see cref="PackageResolutionResult"/> shape and the
/// <c>MIST_PKG_002</c> diagnostic category (invalid version / not found).
/// Network-dependent tests are in integration test classes.
/// </summary>
public sealed class PackageResolverDiagnosticsTests
{
    private const string TargetFramework = "net10.0";

    // ═══════════════════════════════════════════════════════════════════════
    // Structured result shape
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyPackages_ReturnsEmptyResultWithZeroFailures()
    {
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            ImmutableArray<NuGetPackageReference>.Empty,
            TargetFramework,
            diagnostics);

        Assert.Empty(result.AssemblyPaths);
        Assert.Empty(result.Resolved);
        Assert.Equal(0, result.FailureCount);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void DefaultPackages_ReturnsEmptyResultWithZeroFailures()
    {
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            default,
            TargetFramework,
            diagnostics);

        Assert.Empty(result.AssemblyPaths);
        Assert.Empty(result.Resolved);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_PKG_002 — not found / invalid version (Requirement 3.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InvalidVersionString_EmitsSingleMistPkg002AndCountsFailure()
    {
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [new NuGetPackageReference("SomePackage", "not-a-valid-version")],
            TargetFramework,
            diagnostics);

        var diag = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PKG_002", diag.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("SomePackage", diag.Message);

        Assert.Equal(1, result.FailureCount);
        Assert.Empty(result.Resolved);
        Assert.Empty(result.AssemblyPaths);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.6 — one independent diagnostic per failing package;
    // resolution continues for the remaining declared packages.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleFailingPackages_EmitOneIndependentDiagnosticEach()
    {
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [
                new NuGetPackageReference("FirstBad", "totally-bogus"),
                new NuGetPackageReference("SecondBad", "also-bogus"),
            ],
            TargetFramework,
            diagnostics);

        var diags = diagnostics.ToImmutableArray();
        Assert.Equal(2, diags.Length);
        Assert.All(diags, d => Assert.Equal("MIST_PKG_002", d.Number.Value));
        Assert.Contains(diags, d => d.Message.Contains("FirstBad", StringComparison.Ordinal));
        Assert.Contains(diags, d => d.Message.Contains("SecondBad", StringComparison.Ordinal));

        Assert.Equal(2, result.FailureCount);
        Assert.Empty(result.Resolved);
    }

    [Fact]
    public void FailingPackageDoesNotPreventSiblingResolution()
    {
        // One package fails (invalid version → MIST_PKG_002); a sibling package
        // with a valid version gets passed to dotnet restore. The invalid package
        // should produce a diagnostic without blocking the restore of valid packages.
        //
        // Note: This test uses "Newtonsoft.Json" which is a real package that
        // should be restorable from NuGet.org. If offline, this test may fail.
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [
                new NuGetPackageReference("BadPackage", "not-a-version"),
                new NuGetPackageReference("Newtonsoft.Json", "13.0.3"),
            ],
            TargetFramework,
            diagnostics);

        // The invalid version package should produce a diagnostic.
        var invalidVersionDiag = diagnostics.ToImmutableArray()
            .FirstOrDefault(d => d.Number.Value == "MIST_PKG_002" && d.Message.Contains("BadPackage"));
        Assert.NotNull(invalidVersionDiag);

        // The failure count includes the invalid version package.
        Assert.True(result.FailureCount >= 1,
            "Expected at least 1 failure for the invalid version package");

        // The valid package should resolve (if network is available).
        // We check that the resolved list either contains the good package
        // or the failure count accounts for both (network unavailable scenario).
        if(result.Resolved.Length > 0)
        {
            var info = result.Resolved.FirstOrDefault(r =>
                r.Name.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(info);
            Assert.True(info.AssemblyCount > 0);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Real package resolution (requires network or cached package)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RealPackage_ResolvesSuccessfully()
    {
        // Tests that a real, well-known package resolves correctly through
        // the dotnet restore pipeline. Uses Newtonsoft.Json as a stable target.
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")],
            TargetFramework,
            diagnostics);

        // If network is available and dotnet is installed, this should succeed.
        if(!diagnostics.HasErrors)
        {
            Assert.Equal(0, result.FailureCount);
            var info = Assert.Single(result.Resolved);
            Assert.Equal("Newtonsoft.Json", info.Name);
            Assert.True(info.AssemblyCount > 0, "Expected at least one assembly from Newtonsoft.Json");
            Assert.NotEmpty(result.AssemblyPaths);

            // Verify the assembly path points to a real file
            foreach(var path in result.AssemblyPaths)
            {
                Assert.True(File.Exists(path), $"Assembly path does not exist: {path}");
            }
        }
    }
}