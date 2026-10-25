namespace Mist.Common.Diagnostics;

/// <summary>
/// Computes the effective severity of an occurrence once, at admission, from its descriptor's
/// default and the operation's policy, and says why it changed when it did. Severity only ever
/// rises: a Fixed descriptor keeps its default, and a warning becomes an error when its number or
/// every warning is promoted.
/// </summary>
public static class SeverityPolicy
{
    public static (DiagnosticSeverity Effective, string? Reason) Apply(MistDiagnostic descriptor, DiagnosticPolicy policy)
    {
        if(descriptor.Policy == PolicyClass.Fixed || descriptor.DefaultSeverity == DiagnosticSeverity.Error)
        {
            return (descriptor.DefaultSeverity, null);
        }

        if(policy.Promoted.Contains(descriptor.Number))
        {
            return (DiagnosticSeverity.Error, $"promoted to an error by warnings_as_errors naming {descriptor.Number.Value} in mist.toml");
        }

        if(policy.PromoteAllWarnings && descriptor.DefaultSeverity == DiagnosticSeverity.Warning)
        {
            return (DiagnosticSeverity.Error, "promoted to an error by warnings_as_errors = true in mist.toml");
        }

        return (descriptor.DefaultSeverity, null);
    }
}