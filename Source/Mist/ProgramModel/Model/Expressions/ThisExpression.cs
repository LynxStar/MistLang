using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

internal sealed class ThisExpression(
    SyntaxNode node,
    IMistType type) : Expression(node)
{
    public override IMistType Type { get; } = type;
}