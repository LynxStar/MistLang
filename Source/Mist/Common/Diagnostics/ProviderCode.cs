namespace Mist.Common.Diagnostics;

/// Provider-qualified codes stay distinct from <see cref="DiagnosticNumber"/> when wrapped by a Mist occurrence.
public readonly record struct ProviderCode(string Provider, string Code, DiagnosticSeverity NativeSeverity);