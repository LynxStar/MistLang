using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A for loop: <c>for (initializer; condition; iterator) { body }</c>.
/// </summary>
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