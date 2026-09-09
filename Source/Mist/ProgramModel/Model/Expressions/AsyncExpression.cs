using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// The <c>async</c> functor applied to an expression. It does one of two things depending on what it is
/// applied to, and the two are the same idea seen from either side: applied to a SYNC operand it lifts
/// the value into a task, and applied to one that is already a task it suppresses the await a sync
/// context would otherwise have inserted, yielding the task itself.
/// </summary>
internal sealed class AsyncExpression(SyntaxNode node, Expression operand, IMistType type, bool isLift)
    : Expression(node)
{
    public Expression Operand { get; } = operand;

    /// <summary>
    /// Whether the operand had to be lifted. False means the operand was already a task and this
    /// expression only kept it from being awaited, which needs nothing emitted at all.
    /// </summary>
    public bool IsLift { get; } = isLift;

    public override IMistType Type { get; } = type;
}