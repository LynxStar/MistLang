using Mist.ProgramModel.Model;

namespace Mist.ProgramAnalysis.Flow;

/// <summary>
/// One event on a path. Evaluate carries an expression whose operands are already evaluated, or a
/// statement that is reached -- a return, a break, one the binder left unsupported -- and an
/// assignment's target is evaluated only for a coalescing read. IsStorageReceiver marks navigation
/// through a value's storage, which does not require its sibling fields to be assigned. The other three
/// each bring a <see cref="Variable"/> into being: Declare carries the LocalDeclarationStatement, whose local is
/// assigned only when it has an initializer; Introduce carries the construct that gives its variable a
/// value outright -- a foreach, a deconstruction, a with block, a lambda's parameter list; Bind
/// carries a pattern, and sits only on the path where the pattern matched. Leave carries the block, or
/// the for statement whose initializer declared a local, whose scope ends here, on every path out of it
/// -- the fall-through, a return, a break -- so that
/// whatever a declaration in it started, a lease or a drop obligation, has a step to end at. Assume
/// carries a condition and sits at the head of each path out of it, <see cref="Holds"/> saying which:
/// what a rule may take as true from here on, or as false.
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