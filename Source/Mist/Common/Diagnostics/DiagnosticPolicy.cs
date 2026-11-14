using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// Applies <see cref="PolicyClass"/> permissions; disabling an Optional rule prevents it from running.
public sealed record DiagnosticPolicy(
    bool PromoteAllWarnings,
    ImmutableHashSet<DiagnosticNumber> Promoted,
    ImmutableHashSet<DiagnosticNumber> Disabled,
    ImmutableHashSet<DiagnosticNumber> Enabled)
{
    public string PromotionSource { get; init; } = "mist.toml";

    public static readonly DiagnosticPolicy Default = new(false, [], [], []);

    public bool IsEnabled(MistDiagnostic descriptor)
    {
        if(descriptor.Policy != PolicyClass.Optional)
        {
            return true;
        }

        return descriptor.EnabledByDefault
            ? !Disabled.Contains(descriptor.Number)
            : Enabled.Contains(descriptor.Number);
    }
}