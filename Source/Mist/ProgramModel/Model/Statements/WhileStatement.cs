using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A while loop: <c>while (condition) { body }</c>.
/// </summary>
internal sealed class WhileStatement(Expression condition, Block body, bool conditionBinds = false) : Statement
{
    public Expression Condition { get; } = condition;

    public Block Body { get; } = body;

    /// <summary>
    /// Whether a pattern in the condition introduced a binding. See
    /// <see cref="IfStatement.ConditionBinds"/> for what emission does with it and why.
    /// </summary>
    public bool ConditionBinds { get; } = conditionBinds;
}