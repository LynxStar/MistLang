using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

public sealed class NuGetRestoreFailureTests
{
    [Fact]
    [Trait("Tier", "1")]
    public void TheAdapterReadsOnlyProtocolSeverityAndProviderCode()
    {
        const string first = "/work/restore.csproj : warning NU1901: arbitrary upstream wording";
        const string second = "error NU1102: another package name need not appear";

        var entries = NuGetRestoreFailure.Parse(first, second);

        Assert.Collection(entries,
            entry =>
            {
                Assert.Equal(new ProviderCode("nuget", "NU1901", DiagnosticSeverity.Warning), entry.Code);
                Assert.Equal(first, entry.Message);
            },
            entry =>
            {
                Assert.Equal(new ProviderCode("nuget", "NU1102", DiagnosticSeverity.Error), entry.Code);
                Assert.Equal(second, entry.Message);
            });
        Assert.Empty(NuGetRestoreFailure.Parse("prose mentions NU1101 and a package", ""));
    }

    [Theory]
    [InlineData("NU1101")]
    [InlineData("NU1102")]
    [InlineData("NU1103")]
    [Trait("Tier", "2")]
    public void AMissingPackageCodeCarriesItsNativeFactsWithoutAttributingFromProse(string code)
    {
        var diagnostics = new DiagnosticBag();
        var packages = new[] { new NuGetPackageReference("First", "1.0.0"), new NuGetPackageReference("Second", "2.0.0") };
        var raw = $"error {code}: only First appears here";

        var result = NuGetRestoreFailure.Report([.. packages], raw, "", diagnostics);

        Assert.Equal(2, result.FailureCount);
        Assert.Equal(2, diagnostics.Admitted.Count);
        for(var i = 0; i < packages.Length; i++)
        {
            var finding = diagnostics.Admitted[i];
            Assert.Same(CommonDiagnostics.PackageNotFound, finding.Descriptor);
            Assert.Equal(new ProviderCode("nuget", code, DiagnosticSeverity.Error), finding.Native);
            var package = Assert.IsType<OptionalValue>(finding.Arguments[0]);
            Assert.Equal(packages[i].Name, Assert.IsType<NameValue>(package.Value).Name);
            Assert.Equal(raw, Assert.IsType<TextValue>(finding.Arguments[^1]).Text);
            Assert.Equal($"Package '{packages[i].Name}' version '{packages[i].Version}' was not found on any configured NuGet feed. Searched: https://api.nuget.org/v3/index.json.", finding.Message);
        }
    }

    [Theory]
    [InlineData("error NU1301: opaque", true)]
    [InlineData("error NU9999: network timeout connection", false)]
    [InlineData("network timeout connection", false)]
    [Trait("Tier", "2")]
    public void ASourceFailureIsClassifiedByItsCodeAndOtherwiseKeepsTheRawExcerpt(string raw, bool network)
    {
        var diagnostics = new DiagnosticBag();

        var result = NuGetRestoreFailure.Report([new NuGetPackageReference("A", "1.0.0")], "", raw, diagnostics);

        Assert.Equal(1, result.FailureCount);
        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Equal(network ? $"Connection or network failure while restoring packages: {raw}" : raw, finding.Message);
        Assert.Equal(raw, Assert.IsType<TextValue>(finding.Arguments[^1]).Text);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void NoOutputKeepsTheExistingPerPackageFallback()
    {
        var diagnostics = new DiagnosticBag();

        var result = NuGetRestoreFailure.Report([new NuGetPackageReference("A", "1.0.0")], "", "", diagnostics);

        Assert.Equal(1, result.FailureCount);
        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNotFound, finding.Descriptor);
        Assert.Null(finding.Native);
        Assert.Equal("Package 'A' version '1.0.0' was not found on any configured NuGet feed. Searched: https://api.nuget.org/v3/index.json.", finding.Message);
    }
    [Fact]
    [Trait("Tier", "2")]
    public void AnExpectedLaunchFailureHasAWrapperAndItsOperationContext()
    {
        var diagnostics = new DiagnosticBag();

        var outcome = NuGetRestoreFailure.Invoke(
            () => throw new System.ComponentModel.Win32Exception("missing tool"), "/work/restore.csproj", diagnostics);

        Assert.Null(outcome);
        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNetworkError, finding.Descriptor);
        Assert.Equal("missing tool", finding.Message);
        var frame = Assert.IsType<OperationFrame>(Assert.Single(finding.Frames));
        Assert.Equal("/work/restore.csproj", frame.Project);
        Assert.Empty(frame.ReferenceChain);
    }

}