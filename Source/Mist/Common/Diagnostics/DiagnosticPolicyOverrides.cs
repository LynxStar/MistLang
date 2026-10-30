using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

public sealed record DiagnosticPolicyOverrides(
    bool? PromoteAll = null,
    ImmutableHashSet<DiagnosticNumber>? Promoted = null,
    ImmutableHashSet<DiagnosticNumber>? Disabled = null,
    ImmutableHashSet<DiagnosticNumber>? Enabled = null)
{
    public DiagnosticPolicy Apply(DiagnosticPolicy manifest)
    {
        return manifest with
        {
            PromoteAllWarnings = PromoteAll ?? manifest.PromoteAllWarnings,
            Promoted = Promoted ?? (PromoteAll.HasValue ? [] : manifest.Promoted),
            Disabled = manifest.Disabled.Union(Disabled ?? []).Except(Enabled ?? []),
            Enabled = manifest.Enabled.Union(Enabled ?? []).Except(Disabled ?? []),
            PromotionSource = PromoteAll.HasValue || Promoted is not null ? "command line" : manifest.PromotionSource,
        };
    }
}