using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>An assembly the program references by path or as a built C# project, with the managed files found beside it.</summary>
public sealed record ExternalAssembly(
    string Name,
    string Path,
    ImmutableArray<string> Closure,
    string? RuntimeConfigPath,
    ReferenceKind Kind);