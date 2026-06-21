namespace Mist.Driver.Contracts;

/// <summary>
/// A single entry from the <c>[csproj_properties]</c> table in <c>mist.toml</c>.
/// Carries an arbitrary MSBuild property key-value pair to be emitted into the
/// generated <c>.csproj</c> file's <c>&lt;PropertyGroup&gt;</c>.
/// </summary>
/// <param name="Key">
/// The MSBuild property name (TOML key). Must match the pattern
/// <c>^[a-zA-Z0-9\-_\.]+$</c> and not exceed 128 characters.
/// </param>
/// <param name="Value">
/// The property value to emit. Must be a non-empty, non-whitespace string.
/// Will be XML-escaped when written to the csproj.
/// </param>
public sealed record CsprojProperty(string Key, string Value);
