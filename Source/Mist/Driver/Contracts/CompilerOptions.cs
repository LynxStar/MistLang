using System.Collections.Immutable;
using Mist.Backends.Contracts;

namespace Mist.Driver.Contracts;

/// <summary>
/// Options controlling a compilation run.
/// </summary>
public sealed record CompilerOptions(
    ImmutableArray<string> SourcePaths,
    BackendId Backend,
    string OutputDirectory,
    ImmutableArray<string> EnabledAnalyzerIds);
