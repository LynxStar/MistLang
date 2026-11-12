namespace Mist.Driver.Contracts;

/// <summary>An entry of the <c>[framework_references]</c> table in <c>mist.toml</c>, naming a .NET shared framework.</summary>
public sealed record FrameworkReference(string Name);