using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

internal sealed class ResultConstructionExpression(
    SyntaxNode node, Expression payload, ConstructedType resultType, ResultCase selectedCase) : Expression(node)
{
    public Expression Payload { get; } = payload;

    public ResultCase Case { get; } = selectedCase;

    public override IMistType Type { get; } = resultType;
}