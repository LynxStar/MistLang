namespace Mist.Driver.Contracts;

/// <summary>A C# project or assembly named in <c>[references]</c>, with the manifest that declares it.</summary>
public sealed record ExternalReference(string Name, string Path, ReferenceKind Kind, string DeclaringManifestPath);