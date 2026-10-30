namespace Mist.Common.Diagnostics;

/// <summary>
/// A diagnostic identity owned by another tool, qualified by that tool. It travels on the Mist
/// occurrence that wraps it and is never turned into a <see cref="DiagnosticNumber"/>.
/// </summary>
public readonly record struct ProviderCode(string Provider, string Code, DiagnosticSeverity NativeSeverity);