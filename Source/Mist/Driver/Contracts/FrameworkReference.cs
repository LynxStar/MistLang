namespace Mist.Driver.Contracts;

/// <summary>
/// A single entry from the <c>[framework_references]</c> table in <c>mist.toml</c>.
/// Identifies a .NET shared framework (e.g., <c>Microsoft.AspNetCore.App</c>) whose
/// reference assemblies should be made available to the project.
/// </summary>
/// <param name="Name">
/// The shared-framework name (TOML key). After trimming, must be 1–256 characters.
/// </param>
public sealed record FrameworkReference(string Name);