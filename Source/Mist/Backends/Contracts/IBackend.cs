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
    BackendResult Emit(ISemanticModel model, string outputDirectory, DiagnosticBag diagnostics);
}
