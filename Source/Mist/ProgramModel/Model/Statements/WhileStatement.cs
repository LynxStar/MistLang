using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A while loop: <c>while (condition) { body }</c>.
/// </summary>
internal sealed class WhileStatement(Expression condition, Block body) : Statement
{
    public Expression Condition { get; } = condition;

    public Block Body { get; } = body;
}