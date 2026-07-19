using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// An if/else statement. The else branch is optional and can be either a block
/// or another <see cref="IfStatement"/> (for else-if chains).
/// </summary>
internal sealed class IfStatement(
    Expression condition,
    Block thenBranch,
    Statement? elseBranch) : Statement
{
    public Expression Condition { get; } = condition;

    public Block ThenBranch { get; } = thenBranch;

    /// <summary>
    /// The else branch: either a <see cref="Block"/> (terminal else) or another
    /// <see cref="IfStatement"/> (else-if chain). Null if no else clause.
    /// </summary>
    public Statement? ElseBranch { get; } = elseBranch;
}