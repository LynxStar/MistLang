using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// What a project asked policy to do: raise every warning or the listed numbers to errors, and
/// switch Optional rules off or on. Nothing here can lower a severity, hide an occurrence or
/// touch a Fixed descriptor; a disabled Optional rule does not run at all.
/// </summary>
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