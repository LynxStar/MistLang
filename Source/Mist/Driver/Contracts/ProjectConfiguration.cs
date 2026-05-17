using System.Collections.Immutable;

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
/// <param name="References">
/// The list of declared project references parsed from the <c>[[references]]</c>
/// array in <c>mist.toml</c>. Defaults to an empty array when no references
/// section is present.
/// </param>
/// <param name="EntryPoint">
/// The optional <c>entry_point</c> field from <c>mist.toml</c>. When set to
/// <c>"top-level"</c>, the project uses a top-level statement file as its entry point.
/// When set to a fully qualified class name, that class's <c>static void Main</c> is used.
/// Defaults to <see langword="null"/> (no explicit entry point configured).
/// </param>
/// <param name="GlobalUsings">
/// The list of namespace strings declared in the <c>[global_usings]</c> section
/// of <c>mist.toml</c>. These namespaces are implicitly imported into every
/// source file within the declaring project. Defaults to an empty array when
/// no <c>[global_usings]</c> section is present.
/// </param>
public sealed record ProjectConfiguration(
    string Name,
    string NamespaceRoot,
    ImmutableArray<ProjectReference> References,
    string? EntryPoint = null,
    ImmutableArray<string> GlobalUsings = default);
