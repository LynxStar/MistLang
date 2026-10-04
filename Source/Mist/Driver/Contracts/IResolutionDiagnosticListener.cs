using Mist.Common.Diagnostics;

namespace Mist.Driver.Contracts;

/// <summary>
/// Called once per Stage 1.5 diagnostic, before Stage 2 and at any verbosity. The instance is also in
/// <see cref="CompilerResult.Diagnostics"/>, so a caller writing both skips by reference.
/// </summary>
public interface IResolutionDiagnosticListener
{
    void Receive(Diagnostic diagnostic);
}