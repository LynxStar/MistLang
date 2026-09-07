using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A value followed by the spread suffix (<c>args...</c>), standing where several arguments go.
/// <para>
/// Bound rather than expanded on the spot because expansion needs the destination: how many slots
/// the tuple owes, and whether its shape is proven, are the CALL's questions and not the operand's.
/// An expression that survives to emission was never expanded and is therefore a spread somewhere
/// that cannot take one.
/// </para>
/// </summary>
internal sealed class SpreadExpression(SyntaxNode node, Expression operand, IMistType type)
    : Expression(node)
{
    public Expression Operand { get; } = operand;

    public override IMistType Type { get; } = type;
}