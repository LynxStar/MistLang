// Feature: csharp-interop-v2, Task 3.3
// In-process tests for the structured PackageResolutionResult shape and each
// resolver diagnostic category.
//
// Validates: Requirements 3.1 (MIST_PKG_001 — no compatible TFM),
//            3.2 (MIST_PKG_002 — not found / invalid version),
//            3.6 (one independent diagnostic per failing package; siblings continue).
//
// These tests are pure in-process and do NOT require network access. Two
// strategies keep them deterministic and offline:
//   1. Invalid NuGet version strings fail NuGetVersion.TryParse before any
//      feed is contacted, exercising the MIST_PKG_002 path with no network.
//   2. Synthetic packages are materialized directly in the local global
//      packages folder (~/.nuget/packages) so PackageResolver's "already
//      cached" short-circuit (Directory.Exists) returns them without a
//      download — exercising the MIST_PKG_001 path and the successful /
//      zero-assembly structured-result shapes offline. Each synthetic package
//      uses a unique GUID-based name and is removed on disposal.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Unit tests for <see cref="PackageResolver.ResolvePackages"/> covering the
/// structured <see cref="PackageResolutionResult"/> shape and the
/// <c>MIST_PKG_001</c> / <c>MIST_PKG_002</c> diagnostic categories. The
/// network-dependent <c>MIST_PKG_003</c> category (connection failure /
/// restore timeout) cannot be exercised deterministically offline and is out
/// of scope for these in-process tests (Requirements 3.3/3.4).
/// </summary>
public sealed class PackageResolverDiagnosticsTests : IDisposable
{
    private const string TargetFramework = "net10.0";

    /// <summary>The standard NuGet global packages folder (~/.nuget/packages/).</summary>
    private static readonly string GlobalPackagesFolder =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");

    /// <summary>
    /// Top-level synthetic package directories created under the global
    /// packages folder, removed when the test instance is disposed.
    /// </summary>
    private readonly List<string> _createdPackageRoots = [];

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a synthetic, already-"cached" package directory under the local
    /// global packages folder so that <see cref="PackageResolver"/> resolves it
    /// without any network access. Optionally populates a single <c>lib/{tfm}/</c>
    /// directory with a placeholder assembly file (the resolver never loads it;
    /// it only enumerates <c>*.dll</c> files).
    /// </summary>
    /// <param name="libTfm">
    /// The <c>lib/</c> target-framework directory to create (e.g., "net8.0" or
    /// "net20"), or <c>null</c> to create a package with no <c>lib/</c> folder.
    /// </param>
    /// <returns>The unique package name and version of the synthetic package.</returns>
    private (string Name, string Version) CreateCachedPackage(string? libTfm)
    {
        // Lowercase, GUID-based name avoids collisions and matches the
        // lowercased directory layout PackageResolver expects.
        var name = $"mist.test.pkg.{Guid.NewGuid():N}";
        const string version = "1.0.0";

        var packageRoot = Path.Combine(GlobalPackagesFolder, name);
        var versionDir = Path.Combine(packageRoot, version);
        Directory.CreateDirectory(versionDir);
        _createdPackageRoots.Add(packageRoot);

        if (libTfm is not null)
        {
            var tfmDir = Path.Combine(versionDir, "lib", libTfm);
            Directory.CreateDirectory(tfmDir);
            // Placeholder assembly — only its *.dll path is enumerated.
            File.WriteAllText(Path.Combine(tfmDir, "TestAssembly.dll"), string.Empty);
        }

        return (name, version);
    }

    public void Dispose()
    {
        foreach (var root in _createdPackageRoots)
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; ignore transient file-lock errors.
            }
        }
    }

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

    [Fact]
    public void CachedPackageWithCompatibleLib_RecordsResolvedInfo()
    {
        var (name, version) = CreateCachedPackage(libTfm: "net8.0");
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [new NuGetPackageReference(name, version)],
            TargetFramework,
            diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(0, result.FailureCount);

        var info = Assert.Single(result.Resolved);
        Assert.Equal(name, info.Name);
        Assert.Equal(version, info.Version);
        Assert.Equal("net8.0", info.SelectedTfm);
        Assert.Equal(1, info.AssemblyCount);

        var assembly = Assert.Single(result.AssemblyPaths);
        Assert.Equal("TestAssembly.dll", Path.GetFileName(assembly));
    }

    [Fact]
    public void CachedPackageWithNoLib_RecordsZeroAssemblyCountWithoutDiagnostic()
    {
        var (name, version) = CreateCachedPackage(libTfm: null);
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [new NuGetPackageReference(name, version)],
            TargetFramework,
            diagnostics);

        // A package that resolves but contributes no compatible assemblies is
        // recorded with AssemblyCount == 0 and produces no error diagnostic.
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(result.AssemblyPaths);

        var info = Assert.Single(result.Resolved);
        Assert.Equal(name, info.Name);
        Assert.Equal(0, info.AssemblyCount);
        Assert.Equal(string.Empty, info.SelectedTfm);
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
        Assert.Equal("MIST_PKG_002", diag.Id.Value);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("SomePackage", diag.Message);

        Assert.Equal(1, result.FailureCount);
        Assert.Empty(result.Resolved);
        Assert.Empty(result.AssemblyPaths);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_PKG_001 — no compatible TFM (Requirement 3.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CachedPackageWithoutCompatibleTfm_EmitsSingleMistPkg001()
    {
        // net20 (.NET Framework 2.0) is not compatible with net10.0.
        var (name, version) = CreateCachedPackage(libTfm: "net20");
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [new NuGetPackageReference(name, version)],
            TargetFramework,
            diagnostics);

        var diag = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PKG_001", diag.Id.Value);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains(name, diag.Message);
        Assert.Contains(TargetFramework, diag.Message);
        // The message lists the framework targets the package offers.
        Assert.Contains("net20", diag.Message);

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
        Assert.All(diags, d => Assert.Equal("MIST_PKG_002", d.Id.Value));
        Assert.Contains(diags, d => d.Message.Contains("FirstBad", StringComparison.Ordinal));
        Assert.Contains(diags, d => d.Message.Contains("SecondBad", StringComparison.Ordinal));

        Assert.Equal(2, result.FailureCount);
        Assert.Empty(result.Resolved);
    }

    [Fact]
    public void FailingPackageDoesNotPreventSiblingResolution()
    {
        // One package fails (invalid version → MIST_PKG_002); a sibling cached
        // package resolves successfully. Resolution must continue past the
        // failure and still record the sibling (Requirement 3.6).
        var (goodName, goodVersion) = CreateCachedPackage(libTfm: "net8.0");
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [
                new NuGetPackageReference("BadPackage", "not-a-version"),
                new NuGetPackageReference(goodName, goodVersion),
            ],
            TargetFramework,
            diagnostics);

        // Exactly one diagnostic for the single failing package.
        var diag = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PKG_002", diag.Id.Value);
        Assert.Contains("BadPackage", diag.Message);
        Assert.Equal(1, result.FailureCount);

        // The sibling still resolved.
        var info = Assert.Single(result.Resolved);
        Assert.Equal(goodName, info.Name);
        Assert.Equal(1, info.AssemblyCount);
        Assert.Single(result.AssemblyPaths);
    }
}
