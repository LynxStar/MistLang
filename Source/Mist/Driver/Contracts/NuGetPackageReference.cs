namespace Mist.Driver.Contracts;

/// <summary>An entry of the <c>[packages]</c> table in <c>mist.toml</c>.</summary>
public sealed record NuGetPackageReference(string Name, string Version);