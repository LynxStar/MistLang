using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

internal static class PackageDiagnosticFacts
{
    private const string Feed = "https://api.nuget.org/v3/index.json";

    public static void InvalidVersion(NuGetPackageReference package, DiagnosticBag diagnostics)
    {
        Missing(package, "", null, diagnostics, invalid: true);
    }

    public static void Missing(
        NuGetPackageReference package, string message, ProviderCode? native, DiagnosticBag diagnostics, bool invalid = false)
    {
        diagnostics.Admit(DiagnosticFactory.Create(
            CommonDiagnostics.PackageNotFound, new Absent(AbsenceReason.Unavailable), diagnostics.Stage,
            [new OptionalValue(SlotKind.Name, new NameValue(package.Name)),
                new OptionalValue(SlotKind.Name, new NameValue(package.Version)),
                new OptionalValue(SlotKind.Path, new PathValue(Feed)),
                Flag(invalid), Flag(!invalid), Flag(false), new TextValue(message)], native: native));
    }

    public static void Network(
        DiagnosticBag diagnostics, string message, ProviderCode? native = null,
        string? assetsMissing = null, string? temporaryProject = null, string? assetsRead = null, bool network = false,
        ImmutableArray<Frame> frames = default)
    {
        var upstream = assetsMissing is null && temporaryProject is null && assetsRead is null && !network;
        diagnostics.Admit(DiagnosticFactory.Create(
            CommonDiagnostics.PackageNetworkError, new Absent(AbsenceReason.Unavailable), diagnostics.Stage,
            [Path(assetsMissing), Path(temporaryProject), Path(assetsRead), Flag(network), Flag(upstream), new TextValue(message)], frames: frames, native: native));
    }

    private static OptionalValue Path(string? value)
    {
        return new OptionalValue(SlotKind.Path, value is null ? null : new PathValue(value));
    }

    private static OptionalValue Flag(bool present)
    {
        return new OptionalValue(SlotKind.Boolean, present ? new BooleanValue(true) : null);
    }
}