using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Shared compilation-wide state passed to all program model builders.
/// </summary>
internal sealed record BuilderContext(
    Scope GlobalScope,
    Model.ProgramModel ProgramModel,
    DiagnosticBag Diagnostics);