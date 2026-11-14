using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// The producing stage belongs to each occurrence because a descriptor can be reported from several stages.
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