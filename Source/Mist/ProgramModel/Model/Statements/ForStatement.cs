using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

internal sealed class ForStatement(
    Statement? initializer,
    Expression? condition,
    Expression? iterator,
    Block body) : Statement
{
    public Statement? Initializer { get; } = initializer;

    public Expression? Condition { get; } = condition;

    public Expression? Iterator { get; } = iterator;

    public Block Body { get; } = body;
}