namespace Mist.Driver.Contracts;

/// <summary>
/// A single entry from the <c>[packages]</c> table in <c>mist.toml</c>.
/// Carries the declared NuGet package name and version string.
/// </summary>
/// <param name="Name">
/// The NuGet package name (TOML key). Must match the pattern
/// <c>^[a-zA-Z0-9\-_\.]+$</c> and not exceed 128 characters.
/// </param>
/// <param name="Version">
/// The exact version string for the package (e.g., <c>"1.2.3"</c>).
/// Must be non-empty and non-whitespace.
/// </param>
public sealed record NuGetPackageReference(string Name, string Version);