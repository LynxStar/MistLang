using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

public sealed class ManifestFeedbackTests
{
    private const string ManifestPath = "/project/mist.toml";

    private static Diagnostic DuplicateUsing()
    {
        return Manifest(DriverDiagnostics.GlobalUsingDuplicateNamespace, "Standard");
    }

    private static Diagnostic InvalidFrameworkValue()
    {
        return Manifest(DriverDiagnostics.FrameworkReferenceInvalidValue, "Microsoft.NotAFramework");
    }

    private static Diagnostic InvalidPackageVersion()
    {
        return Manifest(DriverDiagnostics.PackageInvalidVersion, "Newtonsoft.Json");
    }

    private static Diagnostic Manifest(DiagnosticDescriptor descriptor, string entry)
    {
        var bag = new DiagnosticBag();
        bag.Report(descriptor, default, ManifestPath, entry, ManifestPath);
        return Assert.Single(bag.ToImmutableArray());
    }

    private static string[] Lines(string text)
    {
        return text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static (bool Stop, string Error) Report(params Diagnostic[] diagnostics)
    {
        using var error = new StringWriter();
        var stop = ManifestFeedback.Report([.. diagnostics], error);
        return (stop, error.ToString());
    }

    private static (int ExitCode, string Output, string Error) Restore(
        ProjectConfiguration? configuration,
        params Diagnostic[] manifest)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = DependencyRestore.Run("/project", configuration, [.. manifest], output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static ProjectConfiguration Clean()
    {
        return new ProjectConfiguration("HelloMist", "HelloMist", []);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void NoDiagnostics_WritesNothingAndContinues()
    {
        var (stop, error) = Report();

        Assert.False(stop);
        Assert.Empty(error);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void Warning_IsWrittenOnceAndDoesNotStop()
    {
        var warning = DuplicateUsing();

        var (stop, error) = Report(warning);

        Assert.False(stop);
        Assert.Equal([DiagnosticFormatter.Format(warning)], Lines(error));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(true)]
    [InlineData(false)]
    public void Error_IsWrittenOnceAndStops(bool framework)
    {
        var diagnostic = framework ? InvalidFrameworkValue() : InvalidPackageVersion();

        var (stop, error) = Report(diagnostic);

        Assert.True(stop);
        Assert.Equal([DiagnosticFormatter.Format(diagnostic)], Lines(error));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void WarningThenError_BothAreWrittenOnceInOrderAndStop()
    {
        var warning = DuplicateUsing();
        var failure = InvalidFrameworkValue();

        var (stop, error) = Report(warning, failure);

        Assert.True(stop);
        Assert.Equal([DiagnosticFormatter.Format(warning), DiagnosticFormatter.Format(failure)], Lines(error));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void Restore_ManifestError_FailsBeforeResolvingAndPrintsNoSummary()
    {
        var failure = InvalidFrameworkValue();
        var configuration = Clean() with { Packages = [new NuGetPackageReference("BadPkg", "invalid-version-string")] };

        var (exitCode, output, error) = Restore(configuration, failure);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal([DiagnosticFormatter.Format(failure)], Lines(error));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void Restore_ManifestWarning_IsWrittenOnceAndRestoreContinues()
    {
        var warning = DuplicateUsing();

        var (exitCode, output, error) = Restore(Clean(), warning);

        Assert.Equal(0, exitCode);
        Assert.Equal(["0 succeeded, 0 failed"], Lines(output));
        Assert.Equal([DiagnosticFormatter.Format(warning)], Lines(error));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void Restore_NoConfiguration_WritesTheParserErrorAndFails()
    {
        var bag = new DiagnosticBag();
        bag.Report(DriverDiagnostics.ProjectMissingName, default, ManifestPath, ManifestPath);
        var failure = Assert.Single(bag.ToImmutableArray());

        var (exitCode, output, error) = Restore(null, failure);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal([DiagnosticFormatter.Format(failure)], Lines(error));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void Restore_CleanManifest_MatchesTheRestoreWithoutManifestDiagnostics()
    {
        var withManifest = Restore(Clean());

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = DependencyRestore.Run("/project", Clean(), output, error);

        Assert.Equal(0, withManifest.ExitCode);
        Assert.Equal((exitCode, output.ToString(), error.ToString()), withManifest);
    }
}