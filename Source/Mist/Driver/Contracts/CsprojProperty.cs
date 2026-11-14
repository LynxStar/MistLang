namespace Mist.Driver.Contracts;

/// <summary>
/// One <c>[csproj_properties]</c> entry from <c>mist.toml</c>, validated by <see cref="ProjectFileParser"/>
/// and emitted into the generated <c>.csproj</c>.
/// </summary>
public sealed record CsprojProperty(string Key, string Value);