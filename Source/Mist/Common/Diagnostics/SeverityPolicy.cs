namespace Mist.Common.Diagnostics;

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
            return (DiagnosticSeverity.Error, $"promoted to an error by warnings_as_errors naming {descriptor.Number.Value} in {policy.PromotionSource}");
        }

        if(policy.PromoteAllWarnings)
        {
            return (DiagnosticSeverity.Error, $"promoted to an error by warnings_as_errors = true in {policy.PromotionSource}");
        }

        return (descriptor.DefaultSeverity, null);
    }
}