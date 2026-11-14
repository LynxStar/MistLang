using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

internal sealed class IfStatement(
    Expression condition,
    Block thenBranch,
    Statement? elseBranch,
    bool conditionBinds = false) : Statement
{
    public Expression Condition { get; } = condition;

    public Block ThenBranch { get; } = thenBranch;

    /// <summary>
    /// A <see cref="Block"/>, or another <see cref="IfStatement"/> for an else-if chain.
    /// </summary>
    public Statement? ElseBranch { get; } = elseBranch;

    /// <summary>
    /// Whether a pattern in the condition introduced a binding. Recorded because Mist and C# scope such
    /// a binding DIFFERENTLY -- Mist to the guarded branch, C# to the block containing the <c>if</c> --
    /// so emission has to contain it, and only construction knows whether there is anything to contain.
    /// </summary>
    public bool ConditionBinds { get; } = conditionBinds;
}