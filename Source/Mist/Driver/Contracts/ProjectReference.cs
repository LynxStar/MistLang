namespace Mist.Driver.Contracts;

/// <summary>
/// A single entry from the <c>[references]</c> table in <c>mist.toml</c>.
/// Carries the declared reference name (the TOML key) and the resolved path.
/// </summary>
/// <param name="Name">
/// The declared reference name (TOML key). Must match the pattern
/// <c>^[a-zA-Z0-9\-_\.]+$</c> and not exceed 128 characters.
/// Validated by <see cref="ProjectFileParser"/>; compared against the target
/// project's name by <see cref="ProjectReferenceResolver"/>.
/// </param>
/// <param name="Path">
/// Normalized absolute path to the referenced project's <c>mist.toml</c> file,
/// as computed by <see cref="ProjectFileParser"/>.
/// </param>
/// <param name="Kind">
/// What the path names. A bare directory stays a Mist project with <paramref name="IsDirectory"/> set and
/// the directory's <c>mist.toml</c> as its path, because only the resolver lists the directory to decide.
/// </param>
public sealed record ProjectReference(
    string Name,
    string Path,
    ReferenceKind Kind = ReferenceKind.MistProject,
    bool IsDirectory = false);