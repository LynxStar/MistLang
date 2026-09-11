using Mist.ProgramModel.Model;

namespace Mist.ProgramAnalysis.Flow;

/// <summary>
/// One event on a path. Evaluate carries an expression whose operands are already evaluated, or a
/// statement that is reached -- a return, a break, one the binder left unsupported -- and an
/// assignment's target is never evaluated, only the assignment is. The other three each bring a
/// <see cref="Variable"/> into being: Declare carries the LocalDeclarationStatement, whose local is
/// assigned only when it has an initializer; Introduce carries the construct that gives its variable a
/// value outright -- a foreach, a deconstruction, a with block, a lambda's parameter list; Bind
/// carries a pattern, and sits only on the path where the pattern matched.
/// </summary>
internal readonly record struct FlowStep(FlowStepKind Kind, object Node, LocalVariable? Variable = null);

internal enum FlowStepKind
{
    Evaluate,
    Declare,
    Introduce,
    Bind,
}