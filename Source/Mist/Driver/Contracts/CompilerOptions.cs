using System.Collections.Immutable;
using Mist.Backends.Contracts;

namespace Mist.Driver.Contracts;

/// <summary>
/// Options controlling a compilation run.
/// </summary>
/// <param name="ProjectFolder">Root folder of the project being compiled.</param>
/// <param name="SourcePaths">Paths to source files to compile.</param>
/// <param name="Backend">The backend to use for code generation.</param>
/// <param name="OutputDirectory">Directory for intermediate output artifacts.</param>
/// <param name="BinDirectory">Directory for final binary output.</param>
/// <param name="ProjectName">Name of the project.</param>
/// <param name="NamespaceRoot">Root namespace for the project.</param>
/// <param name="EnabledAnalyzerIds">IDs of analyzers to run during compilation.</param>
/// <param name="EmitToDisk">Whether to write output files to disk.</param>
/// <param name="GlobalUsings">
/// Namespaces to implicitly import into every compilation unit.
/// Enables in-process test compilation with global usings without requiring a mist.toml on disk.
/// </param>
public sealed record CompilerOptions(
    string ProjectFolder,
    ImmutableArray<string> SourcePaths,
    BackendId Backend,
    string OutputDirectory,
    string BinDirectory,
    string ProjectName,
    string NamespaceRoot,
    ImmutableArray<string> EnabledAnalyzerIds,
    bool EmitToDisk = true,
    ImmutableArray<string> GlobalUsings = default);
