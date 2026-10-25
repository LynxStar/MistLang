using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// The resolver's validation and its restore boundary wired as the driver wires them: the restore is a stub
/// that records what the listener held when it was entered, then fails as a restore that found nothing would.
/// </summary>
[Trait("Tier", "2")]
public sealed class PackageRestoreDeliveryTests
{
    [Fact]
    public void AnInvalidVersionReachesTheListenerBeforeTheRestoreOfItsSiblingBegins()
    {
        var listener = new RecordingListener();
        var resolution = new ResolutionDiagnostics(0, listener);
        string[]? heldAtRestore = null;

        var result = PackageResolver.ResolvePackages(
            [new NuGetPackageReference("BadPkg", "not-valid"), new NuGetPackageReference("Bogus", "35.5.0")],
            [],
            "net10.0",
            resolution.Reported,
            resolution.Deliver,
            (_, _) =>
            {
                heldAtRestore = [.. listener.Received.Select(diagnostic => diagnostic.Message)];
                return (1, "error NU1101: Unable to find package Bogus.", "");
            });

        var held = Assert.Single(heldAtRestore!);
        Assert.Contains("'BadPkg' version 'not-valid'", held, StringComparison.Ordinal);
        Assert.Equal(2, result.FailureCount);
    }

    [Fact]
    public void TheStageBoundaryDeliveryAddsWhatTheRestoreAndFrameworksReported_AndRepeatsNothing()
    {
        var listener = new RecordingListener();
        var resolution = new ResolutionDiagnostics(0, listener);
        var framework = DiagnosticFixtures.Occurrence("MIST_FWREF_003", DiagnosticSeverity.Warning, PipelineStage.Driver, "Framework 'C' probe.", default, "", null);

        _ = PackageResolver.ResolvePackages(
            [new NuGetPackageReference("BadPkg", "not-valid"), new NuGetPackageReference("Bogus", "35.5.0")],
            [],
            "net10.0",
            resolution.Reported,
            resolution.Deliver,
            (_, _) => (1, "error NU1101: Unable to find package Bogus.", ""));
        resolution.Reported.Add(framework);
        resolution.Deliver();
        resolution.Deliver();

        Assert.Equal(resolution.Reported.ToImmutableArray(), listener.Received);
        Assert.Collection(
            listener.Received,
            diagnostic => Assert.Contains("'BadPkg'", diagnostic.Message, StringComparison.Ordinal),
            diagnostic => Assert.Contains("'Bogus'", diagnostic.Message, StringComparison.Ordinal),
            diagnostic => Assert.Same(framework, diagnostic));
    }

    [Fact]
    public void WithOnlyInvalidVersionsNoRestoreRunsAndTheBoundaryDeliversThem()
    {
        var listener = new RecordingListener();
        var resolution = new ResolutionDiagnostics(0, listener);
        var restored = false;

        _ = PackageResolver.ResolvePackages(
            [new NuGetPackageReference("BadPkg", "not-valid")],
            [],
            "net10.0",
            resolution.Reported,
            resolution.Deliver,
            (_, _) =>
            {
                restored = true;
                return (0, "", "");
            });

        Assert.False(restored);
        Assert.Empty(listener.Received);
        resolution.Deliver();
        _ = Assert.Single(listener.Received);
    }

    private sealed class RecordingListener : IResolutionDiagnosticListener
    {
        public List<Diagnostic> Received { get; } = [];

        public void Receive(Diagnostic diagnostic)
        {
            Received.Add(diagnostic);
        }
    }
}