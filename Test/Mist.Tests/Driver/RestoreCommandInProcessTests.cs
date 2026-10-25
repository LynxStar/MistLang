// Feature: csharp-interop-v2, Task 8.4
// In-process tests for `mist restore`: DependencyRestore, the code the command runs, with its
// output and error streams captured.
//
// Validates: Requirements 10.2, 13.2, 13.3, 13.4, 13.5, 13.7, 13.8

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// The restore command's report. The Newtonsoft.Json tests run a real <c>dotnet restore</c> and need
/// network access or a pre-cached copy in ~/.nuget/packages; the rest are refused before any process
/// starts, an invalid version by its syntax and an unknown framework by its name.
/// </summary>
public sealed class RestoreCommandInProcessTests
{
    private const string TargetFramework = "net10.0";

    private static (int ExitCode, string Output, string ErrorOutput) Restore(
        ImmutableArray<NuGetPackageReference> packages,
        ImmutableArray<FrameworkReference> frameworkReferences)
    {
        var configuration = new ProjectConfiguration("Probe", "Probe", [], Packages: packages, FrameworkReferences: frameworkReferences);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = DependencyRestore.Run(configuration, output, error);

        return (exitCode, output.ToString(), error.ToString());
    }

    private static string[] Lines(string text)
    {
        return text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    [Trait("Tier", "4")]
    public void RestoreSuccess_RealPackage_ExitZero_OutputContainsRestoredLine()
    {
        var (exitCode, output, errorOutput) = Restore(
            [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")],
            ImmutableArray<FrameworkReference>.Empty);

        Assert.Equal(0, exitCode);
        Assert.Contains("Restored Newtonsoft.Json", output);
        Assert.Contains("1 succeeded, 0 failed", output);
        Assert.Empty(errorOutput);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void RestoreFailure_InvalidVersion_ExitNonZero_MistPkg002()
    {
        var (exitCode, output, errorOutput) = Restore(
            [new NuGetPackageReference("BadPkg", "invalid-version-string")],
            ImmutableArray<FrameworkReference>.Empty);

        Assert.Equal(1, exitCode);
        Assert.Contains("0 succeeded, 1 failed", output);
        Assert.StartsWith("error MIST0036: Package 'BadPkg'", errorOutput);
    }

    [Fact]
    [Trait("Tier", "4")]
    public void RestoreMixed_OneGoodOneBad_ShowsMixedSummary()
    {
        var (exitCode, output, errorOutput) = Restore(
            [
                new NuGetPackageReference("Newtonsoft.Json", "13.0.3"),
                new NuGetPackageReference("NonExistentPkg", "bad-version"),
            ],
            ImmutableArray<FrameworkReference>.Empty);

        Assert.Equal(1, exitCode);
        Assert.Contains("Restored Newtonsoft.Json", output);
        Assert.Contains("1 succeeded, 1 failed", output);
        Assert.Contains("MIST0036", errorOutput);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void RestoreFailure_DependenciesSharingAnIdAndAnUnknownFramework_EachReportedOnce()
    {
        var (exitCode, output, errorOutput) = Restore(
            [new NuGetPackageReference("BadA", "not-valid"), new NuGetPackageReference("BadB", "also-not-valid")],
            [new FrameworkReference("Not.A.Framework")]);

        Assert.Equal(1, exitCode);
        Assert.Equal(["0 succeeded, 3 failed"], Lines(output));
        var errors = Lines(errorOutput);
        Assert.Equal(3, errors.Length);
        Assert.StartsWith("error MIST0036: Package 'BadA'", errors[0]);
        Assert.StartsWith("error MIST0036: Package 'BadB'", errors[1]);
        Assert.StartsWith("error MIST0035: Framework reference 'Not.A.Framework'", errors[2]);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void Report_WritesDetailNotesAndSummaryToOutput_AndDiagnosticsToError()
    {
        var packages = new PackageResolutionResult(
            [],
            [new ResolvedPackageInfo("Analyzers", "1.0.0", "", 0), new ResolvedPackageInfo("Lib", "2.0.0", "net8.0", 1)],
            FailureCount: 1);
        var frameworks = new FrameworkResolutionResult([], [new ResolvedFrameworkInfo("Microsoft.AspNetCore.App", "10.0.0")], FailureCount: 0);
        var failure = DiagnosticFixtures.Occurrence("TEST_PKG_001", DiagnosticSeverity.Error, PipelineStage.Driver, "Package 'Old' does not support net10.0.", default, "", null);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = DependencyRestore.Report(packages, frameworks, [failure], TargetFramework, output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            [
                "Restored Analyzers 1.0.0 ()",
                "Restored Lib 2.0.0 (net8.0)",
                "Resolved Microsoft.AspNetCore.App 10.0.0",
                "Analyzers contributed no assemblies for net10.0",
                "3 succeeded, 1 failed",
            ],
            Lines(output.ToString()));
        Assert.Equal(["error TEST_PKG_001: Package 'Old' does not support net10.0."], Lines(error.ToString()));
    }

    [Fact]
    [Trait("Tier", "3")]
    public void References_AreReportedByKind_AndOnlyACSharpProjectIsRestored()
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-restore-refs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bridge = Touch(root, "Bridge", "Bridge.csproj");
            var widgets = Touch(root, "libs", "Widgets.dll");
            _ = Touch(root, "App", "mist.toml");
            var restored = new List<string>();
            using var output = new StringWriter();

            var errors = DependencyRestore.RestoreReferences(
                Path.Combine(root, "App"),
                [new ProjectReference("Bridge", bridge, ReferenceKind.CSharpProject), new ProjectReference("Widgets", widgets, ReferenceKind.Assembly)],
                output,
                (project, directory) =>
                {
                    restored.Add(project);
                    Assert.Equal(Path.GetDirectoryName(project), directory);
                    return (0, "", "");
                });

            Assert.Empty(errors);
            Assert.Equal([bridge], restored);
            Assert.Equal([$"Reference Bridge: C# project {bridge}", $"Reference Widgets: assembly {widgets}"], Lines(output.ToString()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AFailedCSharpRestore_IsAnErrorNamingTheReference()
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-restore-refs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bridge = Touch(root, "Bridge", "Bridge.csproj");
            _ = Touch(root, "App", "mist.toml");
            using var output = new StringWriter();

            var errors = DependencyRestore.RestoreReferences(
                Path.Combine(root, "App"),
                [new ProjectReference("Bridge", bridge, ReferenceKind.CSharpProject)],
                output,
                (_, _) => (1, "Bridge.csproj : error NU1101: Unable to find package Nope.", ""));

            Assert.Equal(
                [$"error: dotnet restore of '{bridge}' for reference 'Bridge' failed: Bridge.csproj : error NU1101: Unable to find package Nope."],
                errors);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Touch(string root, string directory, string file)
    {
        var path = Path.Combine(root, directory, file);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, file == "mist.toml" ? "name = \"App\"" : "");
        return path;
    }
}