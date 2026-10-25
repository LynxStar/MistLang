#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors shared across multiple compiler areas.
/// Covers standard library loading, SDK resolution, namespace/type resolution,
/// interop projection, target-typed new expressions, void type restrictions,
/// default keyword semantics, definite assignment, and associated type validation.
/// </summary>
public static class CommonDiagnostics
{
    // ── Standard Library loading diagnostics ─────────────────────────────

    public static readonly MistDiagnostic StdlibParse = new(
        DiagnosticNumber.Mint("MIST0031"),
        Title: "Standard library load failed",
        Template: "Failed to load Standard Library source: {0}: {1}",
        Description: "The compiler could not read a standard library source file, either because the stdlib directory is absent or because a file in it is missing or unreadable. Check the compiler installation and the stdlib location it is configured to use.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── SDK diagnostics ──────────────────────────────────────────────────

    public static readonly MistDiagnostic SdkNotFound = new(
        DiagnosticNumber.Mint("MIST0032"),
        Title: "SDK installation not found",
        Template: "The .NET SDK installation could not be located. Searched: 'dotnet --list-sdks' and well-known paths (/usr/share/dotnet, C:\\Program Files\\dotnet, ~/.dotnet).",
        Description: "No .NET SDK was found on this machine through the dotnet tool or the usual install locations, and the compiler needs one for reference assemblies. Install the .NET SDK, or make the dotnet executable reachable on PATH.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic SdkNoFramework = new(
        DiagnosticNumber.Mint("MIST0033"),
        Title: "Target framework not in SDK",
        Template: "The target framework '{0}' reference assemblies were not found in the SDK at '{1}'.",
        Description: "An SDK was located but it carries no reference assemblies for the framework version the project targets. Install the SDK or targeting pack for that version, or lower the target to one that is installed.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Namespace and type resolution diagnostics ────────────────────────

    // ── Interop diagnostics ──────────────────────────────────────────────

    // ── Framework reference resolution diagnostics ────────────────────────

    public static readonly MistDiagnostic FrameworkReferenceNotInstalled = new(
        DiagnosticNumber.Mint("MIST0034"),
        Title: "Framework reference not installed",
        Template: "Framework reference '{0}' is not installed for {1}. Searched: {2}.",
        Description: "A shared framework enabled under [framework_references] is recognized, but its reference pack for the target framework is absent from every searched location. Install the matching .NET SDK or targeting pack, or disable the reference.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic FrameworkReferenceUnknownName = new(
        DiagnosticNumber.Mint("MIST0035"),
        Title: "Unknown shared framework",
        Template: "Framework reference '{0}' is not a known shared framework.",
        Description: "A name enabled under [framework_references] is absent from the list of shared frameworks the compiler can resolve. Use a known name such as Microsoft.AspNetCore.App or Microsoft.WindowsDesktop.App, or remove the entry.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Package resolution diagnostics ───────────────────────────────────

    public static readonly MistDiagnostic PackageNotFound = new(
        DiagnosticNumber.Mint("MIST0036"),
        Title: "Package not found",
        Template: "{0}",
        Description: "A package listed under [packages] in mist.toml, or the version requested for it, exists on none of the configured NuGet feeds; the message is the restore tool's own. Check the package id and version, and that the feed holding it is configured.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PackageNetworkError = new(
        DiagnosticNumber.Mint("MIST0037"),
        Title: "Package restore network failure",
        Template: "{0}",
        Description: "Restoring a NuGet package failed because a feed could not be reached or did not answer within the time allowed; the message is the restore tool's own. Check the network connection and feed configuration, then build again.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Target-typed new diagnostics ─────────────────────────────────────

    // ── Void type diagnostics ────────────────────────────────────────────

    // ── Default keyword diagnostics ──────────────────────────────────────

    // ── Definite assignment diagnostics ──────────────────────────────────

    // ── Associated type diagnostics ──────────────────────────────────────

    public static readonly MistDiagnostic DuplicateAssociatedTypeSlot = new(
        DiagnosticNumber.Mint("MIST0038"),
        Title: "Duplicate associated type slot",
        Template: "Abstract type '{0}' declares duplicate associated type slot '{1}'",
        Description: "An abstract type names the same associated type slot twice, so a conforming type could not say which one it is filling. Remove or rename one of the two slots.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ModifiersOnAssociatedTypeSlot = new(
        DiagnosticNumber.Mint("MIST0039"),
        Title: "Modifiers on associated type slot",
        Template: "Modifiers are not permitted on associated type slot declarations",
        Description: "An associated type slot in an abstract type carries an access or other modifier, but slots take their visibility from the abstract type and accept none of their own. Delete the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}