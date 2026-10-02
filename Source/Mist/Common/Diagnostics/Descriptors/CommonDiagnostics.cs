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

    /// <summary>Stdlib file missing, unreadable, or the stdlib directory is absent.</summary>
    public static readonly DiagnosticDescriptor StdlibParse = new(
        new DiagnosticId("MIST_STDLIB_PARSE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Failed to load Standard Library source: {0}: {1}");

    // ── SDK diagnostics ──────────────────────────────────────────────────

    /// <summary>The .NET SDK installation could not be located.</summary>
    public static readonly DiagnosticDescriptor SdkNotFound = new(
        new DiagnosticId("MIST_SDK_NOT_FOUND"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The .NET SDK installation could not be located. Searched: 'dotnet --list-sdks' and well-known paths (/usr/share/dotnet, C:\\Program Files\\dotnet, ~/.dotnet).");

    /// <summary>The target framework reference path does not exist in the SDK.</summary>
    public static readonly DiagnosticDescriptor SdkNoFramework = new(
        new DiagnosticId("MIST_SDK_NO_FRAMEWORK"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The target framework '{0}' reference assemblies were not found in the SDK at '{1}'.");

    // ── Namespace and type resolution diagnostics ────────────────────────

    /// <summary>A <c>using</c> directive references a namespace that does not exist.</summary>
    public static readonly DiagnosticDescriptor NamespaceNotFound = new(
        new DiagnosticId("MIST_NS_NOT_FOUND"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Namespace '{0}' could not be found.");

    /// <summary>A type name is ambiguous between multiple imported CLR namespaces.</summary>
    public static readonly DiagnosticDescriptor TypeAmbiguous = new(
        new DiagnosticId("MIST_TYPE_AMBIGUOUS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type name '{0}' is ambiguous between namespaces '{1}' and '{2}'.");

    // ── Interop diagnostics ──────────────────────────────────────────────

    // ── Framework reference resolution diagnostics ────────────────────────

    /// <summary>
    /// An enabled framework reference is a known shared framework but its
    /// reference pack is not installed for the target framework.
    /// </summary>
    public static readonly DiagnosticDescriptor FrameworkReferenceNotInstalled = new(
        new DiagnosticId("MIST_FWREF_001"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Framework reference '{0}' is not installed for {1}. Searched: {2}.");

    /// <summary>
    /// An enabled framework reference name does not correspond to a known
    /// shared framework (it is absent from the known-framework allow-list).
    /// </summary>
    public static readonly DiagnosticDescriptor FrameworkReferenceUnknownName = new(
        new DiagnosticId("MIST_FWREF_002"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Framework reference '{0}' is not a known shared framework.");

    // ── Package resolution diagnostics ───────────────────────────────────

    /// <summary>
    /// A declared package provides no assemblies compatible with the target framework.
    /// </summary>
    public static readonly DiagnosticDescriptor PackageNoCompatibleTfm = new(
        new DiagnosticId("MIST_PKG_001"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Package '{0}' version '{1}' does not provide assemblies compatible with the target framework '{2}'. Available frameworks: {3}.");

    /// <summary>
    /// A referenced NuGet package name or version could not be found on any configured NuGet feed.
    /// </summary>
    public static readonly DiagnosticDescriptor PackageNotFound = new(
        new DiagnosticId("MIST_PKG_002"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "{0}");

    /// <summary>
    /// A connection or network failure occurred while restoring a package, or the restore
    /// did not complete within the timeout bound.
    /// </summary>
    public static readonly DiagnosticDescriptor PackageNetworkError = new(
        new DiagnosticId("MIST_PKG_003"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "{0}");

    // ── Target-typed new diagnostics ─────────────────────────────────────

    // ── Void type diagnostics ────────────────────────────────────────────

    // ── Default keyword diagnostics ──────────────────────────────────────

    // ── Definite assignment diagnostics ──────────────────────────────────

    // ── Associated type diagnostics ──────────────────────────────────────

    /// <summary>An abstract type declares two or more associated type slots with the same name.</summary>
    public static readonly DiagnosticDescriptor DuplicateAssociatedTypeSlot = new(
        new DiagnosticId("MIST_AT_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Abstract type '{0}' declares duplicate associated type slot '{1}'");

    /// <summary>Modifiers are not permitted on associated type slot declarations.</summary>
    public static readonly DiagnosticDescriptor ModifiersOnAssociatedTypeSlot = new(
        new DiagnosticId("MIST_AT_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Modifiers are not permitted on associated type slot declarations");
}