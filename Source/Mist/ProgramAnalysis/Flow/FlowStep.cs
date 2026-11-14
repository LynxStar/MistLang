using Mist.ProgramModel.Model;

namespace Mist.ProgramAnalysis.Flow;

/// <summary>
/// Evaluate follows its operands or marks a reached statement; an assignment target is evaluated only
/// for a coalescing read. IsStorageReceiver navigates storage without requiring sibling fields assigned.
/// Declare carries a LocalDeclarationStatement and assigns <see cref="Variable"/> only with an initializer;
/// Introduce carries a construct that supplies its variable's value; Bind carries a pattern on its matched path.
/// Leave carries the ending block or for-initializer scope on every exit path, so leases and drop obligations
/// can end there. Assume carries a condition at each outgoing path's head; <see cref="Holds"/> gives its truth.
/// </summary>
internal readonly record struct FlowStep(
    FlowStepKind Kind, object Node, LocalVariable? Variable = null, bool Holds = true, bool IsStorageReceiver = false);

internal enum FlowStepKind
{
    Evaluate,
    Declare,
    Introduce,
    Bind,
    Leave,
    Assume,
}