namespace Mist.Driver.Contracts;

/// <summary>
/// Structured representation of a parsed <c>mist.toml</c> project file.
/// Designed for forward-compatible extension with additional fields.
/// </summary>
public sealed record ProjectConfiguration(string Name);
