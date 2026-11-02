using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

internal sealed class ResultIdentityExpression(SyntaxNode node, Expression operand) : Expression(node)
{
    public Expression Operand { get; } = operand;

    public override IMistType Type => Operand.Type;
}