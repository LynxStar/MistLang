using System.Collections.Immutable;
using Mist.Backends.Contracts;

namespace Mist.Driver.Contracts;

/// <summary>
/// Options controlling a compilation run.
/// </summary>
public sealed record CompilerOptions(
    string ProjectFolder,
    ImmutableArray<string> SourcePaths,
    BackendId Backend,
    string OutputDirectory,
    string BinDirectory,
    string ProjectName,
    string NamespaceRoot,
    ImmutableArray<string> EnabledAnalyzerIds);
