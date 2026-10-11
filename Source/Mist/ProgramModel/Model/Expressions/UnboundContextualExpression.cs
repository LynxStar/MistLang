using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Expressions;

internal sealed class UnboundContextualExpression(ExpressionNode syntax) : Expression(syntax)
{
    public ExpressionNode Syntax { get; } = syntax;

    public override IMistType Type => UnboundContextualType.Instance;
}