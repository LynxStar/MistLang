namespace Mist.Driver.Contracts;

/// <summary>
/// Structured representation of a parsed <c>mist.toml</c> project file.
/// Designed for forward-compatible extension with additional fields.
/// </summary>
/// <param name="Name">The project name from the <c>name</c> field.</param>
/// <param name="NamespaceRoot">
/// The root segment for all inferred namespaces. Populated from the optional
/// <c>namespace_root</c> field in <c>mist.toml</c>, defaulting to <paramref name="Name"/>
/// when not specified.
/// </param>
public sealed record ProjectConfiguration(string Name, string NamespaceRoot);
