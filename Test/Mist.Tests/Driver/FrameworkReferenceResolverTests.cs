using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

public sealed class FrameworkReferenceResolverTests
{
    private const string TargetFramework = "net10.0";
    private const string AspNetCore = "Microsoft.AspNetCore.App";

    /// <summary>
    /// Mirrors the resolver's own pack discovery (same dotnet root, TFM-major match and at least
    /// one ref assembly) so installation-dependent tests assert the success path when the pack is
    /// present and MIST0034 when it is absent, passing on any host.
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
            Assert.Empty(result.Resolved);
            Assert.Empty(result.AssemblyPaths);
            Assert.Equal(1, result.FailureCount);

            var diag = Assert.Single(diagnostics.ToImmutableArray());
            Assert.Equal("MIST0034", diag.Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
            Assert.Contains(AspNetCore, diag.Message);
            Assert.Contains(TargetFramework, diag.Message);
        }
    }

    [Fact]
    public void AspNetCore_VersionSelection_MajorMatchesTfmAndIsHighest()
    {
        var (installed, expectedVersion) = ProbeHighestMatchingPack(AspNetCore, TargetFramework);

        // Skip the meaningful selection assertion only when the pack is entirely
        // absent — there is nothing to select. The dedicated MIST0034 test
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

        Assert.Equal(expectedVersion, info.Version);

        Assert.True(Version.TryParse(info.Version, out var selected));
        var tfmMajor = SdkDiscovery.ExtractTfmMajorVersion(TargetFramework);
        Assert.True(tfmMajor.HasValue);
        Assert.Equal(tfmMajor!.Value, selected!.Major);
    }

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
            Assert.Equal("MIST0034", diag.Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
            Assert.Contains(AspNetCore, diag.Message);
            Assert.Contains(unavailableTfm, diag.Message);
            Assert.Contains($"{AspNetCore}.Ref", diag.Message);
        }
    }

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
        Assert.Equal("MIST0035", diag.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains(unknown, diag.Message);
    }

    [Fact]
    public void MixedFailures_EmitOneIndependentDiagnosticEach()
    {
        const string unknown = "Totally.Unknown.Framework";
        const string unavailableTfm = "net5.0";

        var (aspNetInstalled, _) = ProbeHighestMatchingPack(AspNetCore, unavailableTfm);

        var diagnostics = new DiagnosticBag();

        var result = FrameworkReferenceResolver.Resolve(
            [
                new FrameworkReference(unknown),
                new FrameworkReference(AspNetCore),
            ],
            unavailableTfm,
            diagnostics);

        var diags = diagnostics.ToImmutableArray();
        Assert.Contains(diags, d => d.Number.Value == "MIST0035" && d.Message.Contains(unknown, StringComparison.Ordinal));

        if(aspNetInstalled)
        {
            Assert.NotEmpty(result.Resolved);
            Assert.Equal(1, result.FailureCount); // only the unknown framework fails
            _ = Assert.Single(diags); // only MIST0035
        }
        else
        {
            Assert.Empty(result.Resolved);
            Assert.Empty(result.AssemblyPaths);
            Assert.Equal(2, result.FailureCount);
            Assert.Equal(2, diags.Length);
            Assert.Contains(diags, d => d.Number.Value == "MIST0034" && d.Message.Contains(AspNetCore, StringComparison.Ordinal));
        }
    }
}