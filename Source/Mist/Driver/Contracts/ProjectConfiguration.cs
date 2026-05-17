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
    ImmutableArray<string> GlobalUsings = default);
