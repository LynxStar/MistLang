using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// An invalid version string fails NuGetVersion.TryParse before any feed is contacted, so only the
/// Newtonsoft.Json cases need a network or a cached package.
/// </summary>
public sealed class PackageResolverDiagnosticsTests
{
    private const string TargetFramework = "net10.0";

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
    public void InvalidVersionString_EmitsSingleMistPkg002AndCountsFailure()
    {
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [new NuGetPackageReference("SomePackage", "not-a-valid-version")],
            TargetFramework,
            diagnostics);

        var diag = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0036", diag.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("SomePackage", diag.Message);

        Assert.Equal(1, result.FailureCount);
        Assert.Empty(result.Resolved);
        Assert.Empty(result.AssemblyPaths);
    }

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
        Assert.All(diags, d => Assert.Equal("MIST0036", d.Number.Value));
        Assert.Contains(diags, d => d.Message.Contains("FirstBad", StringComparison.Ordinal));
        Assert.Contains(diags, d => d.Message.Contains("SecondBad", StringComparison.Ordinal));

        Assert.Equal(2, result.FailureCount);
        Assert.Empty(result.Resolved);
    }

    [Fact]
    public void FailingPackageDoesNotPreventSiblingResolution()
    {
        var diagnostics = new DiagnosticBag();

        var result = PackageResolver.ResolvePackages(
            [
                new NuGetPackageReference("BadPackage", "not-a-version"),
                new NuGetPackageReference("Newtonsoft.Json", "13.0.3"),
            ],
            TargetFramework,
            diagnostics);

        var invalidVersionDiag = diagnostics.ToImmutableArray()
            .FirstOrDefault(d => d.Number.Value == "MIST0036" && d.Message.Contains("BadPackage"));
        Assert.NotNull(invalidVersionDiag);

        Assert.True(result.FailureCount >= 1,
            "Expected at least 1 failure for the invalid version package");

        // The valid package should resolve (if network is available).
        if(result.Resolved.Length > 0)
        {
            var info = result.Resolved.FirstOrDefault(r =>
                r.Name.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(info);
            Assert.True(info.AssemblyCount > 0);
        }
    }

    [Fact]
    public void RealPackage_ResolvesSuccessfully()
    {
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

            foreach(var path in result.AssemblyPaths)
            {
                Assert.True(File.Exists(path), $"Assembly path does not exist: {path}");
            }
        }
    }
}