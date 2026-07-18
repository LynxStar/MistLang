using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A <c>return</c> statement.
/// </summary>
internal sealed class ReturnStatement(Expression? value) : Statement
{
    /// <summary>
    /// The resolved return value, or null for a bare <c>return;</c>.
    /// </summary>
    public Expression? Value { get; } = value;
}