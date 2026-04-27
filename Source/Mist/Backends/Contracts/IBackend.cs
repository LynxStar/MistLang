using Mist.Common.Diagnostics;
using Mist.Semantics.Contracts;

namespace Mist.Backends.Contracts;

/// <summary>
/// Contract for a pluggable compiler backend that consumes a semantic model
/// and produces a target output artifact.
/// </summary>
public interface IBackend
{
    BackendId Id { get; }
    IAmbientTypeResolver? GetAmbientTypeResolver();
    BackendResult Emit(ISemanticModel model, string outputDirectory, string binDirectory, DiagnosticBag diagnostics);
}
