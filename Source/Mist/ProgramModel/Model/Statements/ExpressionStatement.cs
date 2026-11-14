using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

internal sealed class ExpressionStatement(Expression expression) : Statement
{
    public Expression Expression { get; } = expression;
}