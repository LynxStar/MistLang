using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// The argument slots a package failure carries, against the <c>PackageNotFound</c> and <c>PackageNetworkError</c>
/// descriptors: each optional slot is present exactly when its fact is known, and <c>upstream</c> means only that the
/// restore tool's own message is the whole explanation, so it is absent whenever a more specific cause is.
/// </summary>
[Trait("Tier", "1")]
public sealed class PackageDiagnosticFactsTests
{
    private const string Feed = "https://api.nuget.org/v3/index.json";

    private static readonly OptionalValue Present = new(SlotKind.Boolean, new BooleanValue(true));

    private static readonly OptionalValue NoFlag = new(SlotKind.Boolean, null);

    private static readonly OptionalValue NoPath = new(SlotKind.Path, null);

    [Theory]
    [InlineData(false, "Package 'A' version '1.0.0' was not found on any configured NuGet feed. Searched: " + Feed + ".")]
    [InlineData(true, "Package 'A' version '1.0.0' could not be found on any configured NuGet feed (" + Feed + "); the version string is not a valid NuGet version.")]
    public void AMissingPackageNamesItsPackageVersionAndFeed_AndSetsExactlyOneOfInvalidOrMissing(bool invalid, string message)
    {
        var diagnostics = new DiagnosticBag();
        var native = new ProviderCode("nuget", "NU1102", DiagnosticSeverity.Error);

        PackageDiagnosticFacts.Missing(new NuGetPackageReference("A", "1.0.0"), "", native, diagnostics, invalid);

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNotFound, finding.Descriptor);
        Assert.Equal<SlotValue>(
            [new OptionalValue(SlotKind.Name, new NameValue("A")), new OptionalValue(SlotKind.Name, new NameValue("1.0.0")),
                new OptionalValue(SlotKind.Path, new PathValue(Feed)),
                invalid ? Present : NoFlag, invalid ? NoFlag : Present, NoFlag, new TextValue("")],
            finding.Arguments);
        Assert.Equal(native, finding.Native);
        Assert.Equal(message, finding.Message);
    }

    [Fact]
    public void AnInvalidVersionIsTheInvalidShapeWithNoMessageOrProviderCode()
    {
        var diagnostics = new DiagnosticBag();

        PackageDiagnosticFacts.InvalidVersion(new NuGetPackageReference("A", "not-a-version"), diagnostics);

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Equal<SlotValue>(
            [new OptionalValue(SlotKind.Name, new NameValue("A")), new OptionalValue(SlotKind.Name, new NameValue("not-a-version")),
                new OptionalValue(SlotKind.Path, new PathValue(Feed)), Present, NoFlag, NoFlag, new TextValue("")],
            finding.Arguments);
        Assert.Null(finding.Native);
    }

    [Theory]
    [InlineData("assetsMissing", "Package restore completed but project.assets.json was not generated.")]
    [InlineData("temporaryProject", "Failed to create temporary restore project: tool said")]
    [InlineData("assetsRead", "Failed to parse project.assets.json: tool said")]
    [InlineData("network", "Connection or network failure while restoring packages: tool said")]
    [InlineData("upstream", "tool said")]
    public void ANetworkFailureSetsOnlyTheSlotOfItsKnownCause_AndUpstreamOnlyWhenNoneIsKnown(string cause, string message)
    {
        var diagnostics = new DiagnosticBag();
        var native = new ProviderCode("nuget", "NU1301", DiagnosticSeverity.Error);

        PackageDiagnosticFacts.Network(
            diagnostics, "tool said", native,
            assetsMissing: cause == "assetsMissing" ? "/r/obj/project.assets.json" : null,
            temporaryProject: cause == "temporaryProject" ? "/r" : null,
            assetsRead: cause == "assetsRead" ? "/r/obj/project.assets.json" : null,
            network: cause == "network");

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNetworkError, finding.Descriptor);
        Assert.Equal<SlotValue>(
            [cause == "assetsMissing" ? new OptionalValue(SlotKind.Path, new PathValue("/r/obj/project.assets.json")) : NoPath,
                cause == "temporaryProject" ? new OptionalValue(SlotKind.Path, new PathValue("/r")) : NoPath,
                cause == "assetsRead" ? new OptionalValue(SlotKind.Path, new PathValue("/r/obj/project.assets.json")) : NoPath,
                cause == "network" ? Present : NoFlag,
                cause == "upstream" ? Present : NoFlag,
                new TextValue("tool said")],
            finding.Arguments);
        Assert.Equal(native, finding.Native);
        Assert.Equal(message, finding.Message);
    }
}