using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A <c>return</c> statement.
/// </summary>
internal sealed class ReturnStatement(Expression? value, bool isRef = false) : Statement
{
    /// <summary>
    /// The resolved return value, or null for a bare <c>return;</c>.
    /// </summary>
    public Expression? Value { get; } = value;

    /// <summary>
    /// Whether the value is returned by reference. Read from the statement's own <c>ref</c> rather
    /// than from the enclosing method's, so what emits is what the author wrote -- a disagreement
    /// between the two is a program to reject, not one to silently repair.
    /// </summary>
    public bool IsRef { get; } = isRef;
}