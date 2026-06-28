using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;

namespace Mist.Backends.Contracts;

/// <summary>
/// Contract for a pluggable compiler backend that consumes a semantic model
/// and produces a target output artifact.
/// </summary>
public interface IBackend
{
    BackendId Id { get; }
    IAmbientTypeResolver? GetAmbientTypeResolver(string targetFramework, ImmutableArray<string> additionalAssemblyPaths);
    BackendResult Emit(ISemanticModel model, string outputDirectory, string binDirectory, string projectName, DiagnosticBag diagnostics, ProjectOutputType outputType = ProjectOutputType.Executable);
}