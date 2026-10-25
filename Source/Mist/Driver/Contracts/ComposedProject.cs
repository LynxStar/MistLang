namespace Mist.Driver.Contracts;

/// <summary>A Mist project whose sources are compiled into this compilation's primary output: the root or a Mist reference.</summary>
public sealed record ComposedProject(string Name, string ManifestPath);