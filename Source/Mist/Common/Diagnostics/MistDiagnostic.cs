using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// One registry entry: every static fact about a diagnostic, declared once so that the catalog,
/// the JSON document and the renderer cannot disagree. The producing stage is not here; it is a
/// property of each occurrence, because one descriptor is reported from several stages.
/// </summary>
public sealed record MistDiagnostic(
    DiagnosticNumber Number,
    string Title,
    string Template,
    string Description,
    DiagnosticCategory Category,
    PolicyClass Policy,
    DiagnosticSeverity DefaultSeverity,
    ImmutableArray<Slot> Slots = default,
    ImmutableArray<DiagnosticNumber> Related = default,
    bool EnabledByDefault = true)
{
    public ImmutableArray<Slot> Slots { get; } = Slots.IsDefault ? [] : Slots;

    public ImmutableArray<DiagnosticNumber> Related { get; } = Related.IsDefault ? [] : Related;
}