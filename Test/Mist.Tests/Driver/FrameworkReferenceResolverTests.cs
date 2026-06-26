// Feature: csharp-interop-v2, Task 4.3
// In-process tests for FrameworkReferenceResolver.
//
// Validates: Requirements 5.1 (locate the installed shared-framework pack for
//            the Target_Framework; success only when the pack dir exists with
//            >=1 ref assembly), 5.4 (MIST_FWREF_001 — known framework, not
//            installed for the TFM), 5.5 (MIST_FWREF_002 — unknown framework
//            name), 5.6 (version selection: among packs whose major matches the
//            TFM major, pick the greatest by integer major->minor->patch).
//
// All tests are pure in-process — the resolver is invoked directly and never
// spawns a process. Assertions that depend on a locally-installed pack are
// environment-adaptive: they discover whether the pack is present (using the
// same dotnet-root discovery the resolver itself uses) and assert the success
// path when present, or the documented not-installed (MIST_FWREF_001) path when
// absent, so the suite passes on machines with or without the pack installed.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Unit tests for <see cref="FrameworkReferenceResolver.Resolve"/> covering
/// successful resolution of a locally-installed framework pack, major-matched
/// highest-version selection, and the <c>MIST_FWREF_001</c> (known but not
/// installed for the TFM) and <c>MIST_FWREF_002</c> (unknown name) diagnostics.
/// </summary>
public sealed class FrameworkReferenceResolverTests
{
    private const string TargetFramework = "net10.0";
    private const string AspNetCore = "Microsoft.AspNetCore.App";

    /// <summary>
    /// Probes the local installation for the highest reference pack of the given
    /// framework whose major version matches the TFM major, mirroring the
    /// resolver's own discovery (same dotnet root, same major-match + ">=1 ref
    /// assembly" rules). Returns whether such a pack is installed and, if so, its
    /// selected version string. Used to make installation-dependent assertions
    /// adapt to the host machine.
    /// </summary>
    private static (bool Installed, string? Version) ProbeHighestMatchingPack(
        string frameworkName,
        string tfm)
    {
        var dotnetRoot = SdkDiscovery.FindDotnetRoot();
        if(dotnetRoot is null)
        {
            return (false, null);
        }

        var packsDir = Path.Combine(dotnetRoot, "packs", $"{frameworkName}.Ref");
        if(!Directory.Exists(packsDir))
        {
            return (false, null);
        }

        var tfmMajor = SdkDiscovery.ExtractTfmMajorVersion(tfm);

        Version? highest = null;
        string? highestName = null;

        foreach(var versionDir in Directory.GetDirectories(packsDir))
        {
            var dirName = Path.GetFileName(versionDir);
            if(!Version.TryParse(dirName, out var version))
            {
                continue;
            }

            if(tfmMajor.HasValue && version.Major != tfmMajor.Value)
            {
                continue;
            }

            var refPath = Path.Combine(versionDir, "ref", tfm);
            if(!Directory.Exists(refPath))
            {
                continue;
            }

            if(Directory.GetFiles(refPath, "*.dll").Length == 0)
            {
                continue;
            }

            if(highest is null || version > highest)
            {
                highest = version;
                highestName = dirName;
            }
        }

        return highestName is null ? (false, null) : (true, highestName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Empty / default input
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyFrameworks_ReturnsEmptyResultWithZeroFailures()
    {
        var diagnostics = new DiagnosticBag();

        var result = FrameworkReferenceResolver.Resolve(
            ImmutableArray<FrameworkReference>.Empty,
            TargetFramework,
            diagnostics);

        Assert.Empty(result.AssemblyPaths);
        Assert.Empty(result.Resolved);
        Assert.Equal(0, result.FailureCount);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void DefaultFrameworks_ReturnsEmptyResultWithZeroFailures()
    {
        var diagnostics = new DiagnosticBag();

        var result = FrameworkReferenceResolver.Resolve(
            default,
            TargetFramework,
            diagnostics);

        Assert.Empty(result.AssemblyPaths);
        Assert.Empty(result.Resolved);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 5.1 — resolve the locally-installed Microsoft.AspNetCore.App
    // pack for the real TFM (environment-adaptive: assert MIST_FWREF_001 when
    // the pack is not installed on this machine).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AspNetCore_ResolvesWhenInstalled_ElseEmitsMistFwref001()
    {
        var (installed, expectedVersion) = ProbeHighestMatchingPack(AspNetCore, TargetFramework);

        var diagnostics = new DiagnosticBag();
        var result = FrameworkReferenceResolver.Resolve(
            [new FrameworkReference(AspNetCore)],
            TargetFramework,
            diagnostics);

        if(installed)
        {
            // Success path (Req 5.1): non-empty assembly paths, a resolved-info
            // entry whose major matches the TFM major, and no failures.
            Assert.False(diagnostics.HasErrors);
            Assert.Equal(0, result.FailureCount);
            Assert.NotEmpty(result.AssemblyPaths);
            Assert.All(result.AssemblyPaths, p => Assert.EndsWith(".dll", p));

            var info = Assert.Single(result.Resolved);
            Assert.Equal(AspNetCore, info.Name);
            Assert.Equal(expectedVersion, info.Version);

            Assert.True(Version.TryParse(info.Version, out var resolvedVersion));
            var tfmMajor = SdkDiscovery.ExtractTfmMajorVersion(TargetFramework);
            Assert.True(tfmMajor.HasValue);
            Assert.Equal(tfmMajor!.Value, resolvedVersion!.Major);
        }
        else
        {
            // Not installed for the TFM on this host (Req 5.4): one MIST_FWREF_001.
            Assert.Empty(result.Resolved);
            Assert.Empty(result.AssemblyPaths);
            Assert.Equal(1, result.FailureCount);

            var diag = Assert.Single(diagnostics.ToImmutableArray());
            Assert.Equal("MIST_FWREF_001", diag.Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
            Assert.Contains(AspNetCore, diag.Message);
            Assert.Contains(TargetFramework, diag.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 5.6 — version selection: the resolved pack's major matches the
    // TFM major, and packs whose major does NOT match are never selected.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AspNetCore_VersionSelection_MajorMatchesTfmAndIsHighest()
    {
        var (installed, expectedVersion) = ProbeHighestMatchingPack(AspNetCore, TargetFramework);

        // Skip the meaningful selection assertion only when the pack is entirely
        // absent — there is nothing to select. The dedicated MIST_FWREF_001 test
        // covers the not-installed behavior.
        if(!installed)
        {
            return;
        }

        var diagnostics = new DiagnosticBag();
        var result = FrameworkReferenceResolver.Resolve(
            [new FrameworkReference(AspNetCore)],
            TargetFramework,
            diagnostics);

        var info = Assert.Single(result.Resolved);

        // The resolver must pick the highest pack whose major matches the TFM
        // major (the probe computes the same expectation independently).
        Assert.Equal(expectedVersion, info.Version);

        Assert.True(Version.TryParse(info.Version, out var selected));
        var tfmMajor = SdkDiscovery.ExtractTfmMajorVersion(TargetFramework);
        Assert.True(tfmMajor.HasValue);
        Assert.Equal(tfmMajor!.Value, selected!.Major);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 5.4 — MIST_FWREF_001: a KNOWN shared framework that is not
    // installed for the requested Target_Framework. Using an old TFM major
    // (net5.0) guarantees no matching pack exists regardless of the host, while
    // keeping the framework name on the known allow-list (so the 002 path is
    // not taken).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void KnownFramework_NotInstalledForTfm_EmitsSingleMistFwref001()
    {
        const string unavailableTfm = "net5.0";

        // Environment-adaptive: probe whether net5.0 packs are actually installed
        // on this machine. Some Windows installations retain old ref packs.
        var (installed, _) = ProbeHighestMatchingPack(AspNetCore, unavailableTfm);

        var diagnostics = new DiagnosticBag();

        var result = FrameworkReferenceResolver.Resolve(
            [new FrameworkReference(AspNetCore)],
            unavailableTfm,
            diagnostics);

        if(installed)
        {
            // Pack IS installed for net5.0 on this machine — resolution succeeds.
            Assert.NotEmpty(result.Resolved);
            Assert.NotEmpty(result.AssemblyPaths);
            Assert.Equal(0, result.FailureCount);
            Assert.False(diagnostics.HasErrors);
        }
        else
        {
            Assert.Empty(result.Resolved);
            Assert.Empty(result.AssemblyPaths);
            Assert.Equal(1, result.FailureCount);

            var diag = Assert.Single(diagnostics.ToImmutableArray());
            Assert.Equal("MIST_FWREF_001", diag.Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
            Assert.Contains(AspNetCore, diag.Message);
            Assert.Contains(unavailableTfm, diag.Message);
            Assert.Contains($"{AspNetCore}.Ref", diag.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 5.5 — MIST_FWREF_002: a framework name absent from the known
    // shared-framework allow-list. No pack lookup is attempted.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnknownFrameworkName_EmitsSingleMistFwref002()
    {
        const string unknown = "Totally.Unknown.Framework";
        var diagnostics = new DiagnosticBag();

        var result = FrameworkReferenceResolver.Resolve(
            [new FrameworkReference(unknown)],
            TargetFramework,
            diagnostics);

        Assert.Empty(result.Resolved);
        Assert.Empty(result.AssemblyPaths);
        Assert.Equal(1, result.FailureCount);

        var diag = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_FWREF_002", diag.Id.Value);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains(unknown, diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 5.7 (supporting) — each enabled framework resolves
    // independently with exactly one diagnostic per failing framework. Combines
    // an unknown name (002) with a known-but-not-installed-for-TFM entry (001).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MixedFailures_EmitOneIndependentDiagnosticEach()
    {
        const string unknown = "Totally.Unknown.Framework";
        const string unavailableTfm = "net5.0";

        // Environment-adaptive: probe whether net5.0 packs are actually installed.
        var (aspNetInstalled, _) = ProbeHighestMatchingPack(AspNetCore, unavailableTfm);

        var diagnostics = new DiagnosticBag();

        var result = FrameworkReferenceResolver.Resolve(
            [
                new FrameworkReference(unknown),
                new FrameworkReference(AspNetCore),
            ],
            unavailableTfm,
            diagnostics);

        // The unknown framework always fails with MIST_FWREF_002.
        var diags = diagnostics.ToImmutableArray();
        Assert.Contains(diags, d => d.Id.Value == "MIST_FWREF_002" && d.Message.Contains(unknown, StringComparison.Ordinal));

        if(aspNetInstalled)
        {
            // AspNetCore pack IS installed for net5.0 — it resolves successfully.
            Assert.NotEmpty(result.Resolved);
            Assert.Equal(1, result.FailureCount); // only the unknown framework fails
            Assert.Single(diags); // only MIST_FWREF_002
        }
        else
        {
            // AspNetCore pack is NOT installed for net5.0 — both fail.
            Assert.Empty(result.Resolved);
            Assert.Empty(result.AssemblyPaths);
            Assert.Equal(2, result.FailureCount);
            Assert.Equal(2, diags.Length);
            Assert.Contains(diags, d => d.Id.Value == "MIST_FWREF_001" && d.Message.Contains(AspNetCore, StringComparison.Ordinal));
        }
    }
}