using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Shared compilation-wide state passed to all program model builders.
/// </summary>
internal sealed record BuilderContext(
    Scope GlobalScope,
    Model.ProgramModel ProgramModel,
    DiagnosticBag Diagnostics)
{
    /// <summary>
    /// Extension block AST nodes collected during Phase 1.
    /// Processed in Phase 2 after all types are registered.
    /// </summary>
    public List<PendingExtension> PendingExtensions { get; } = [];

    /// <summary>
    /// With-declaration AST nodes collected during Phase 1.
    /// Processed in Phase 2c after all types and extensions are resolved.
    /// </summary>
    public List<PendingConformance> PendingConformances { get; } = [];
}