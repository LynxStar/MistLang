namespace Mist.Driver.Contracts;

/// <summary>An entry of the <c>[references]</c> table in <c>mist.toml</c>.</summary>
/// <param name="Name">The TOML key; <see cref="ProjectReferenceResolver"/> requires it to equal the name the target produces.</param>
/// <param name="Path">Normalized and absolute.</param>
/// <param name="Kind">
/// What the path names. A bare directory stays a Mist project with <paramref name="IsDirectory"/> set and
/// the directory's <c>mist.toml</c> as its path, because only the resolver lists the directory to decide.
/// </param>
public sealed record ProjectReference(
    string Name,
    string Path,
    ReferenceKind Kind = ReferenceKind.MistProject,
    bool IsDirectory = false);