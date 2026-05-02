namespace Mist.Driver.Contracts;

/// <summary>
/// A single entry from the <c>[[references]]</c> array in <c>mist.toml</c>.
/// Carries the raw path string as written by the user; resolution to a
/// canonical file-system path is performed by <c>ProjectReferenceResolver</c>.
/// </summary>
/// <param name="Path">
/// Normalized absolute path to the referenced project's <c>mist.toml</c> file,
/// as computed by <see cref="ProjectFileParser"/>.
/// </param>
public sealed record ProjectReference(string Path);
