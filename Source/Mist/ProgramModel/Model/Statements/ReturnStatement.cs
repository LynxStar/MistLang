using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A <c>return</c> statement.
/// </summary>
internal sealed class ReturnStatement(Expression? value, IMistType? targetType, bool isRef = false) : Statement
{
    /// <summary>
    /// The resolved return value, or null for a bare <c>return;</c>.
    /// </summary>
    public Expression? Value { get; } = value;

    /// <summary>
    /// The type the value was bound against: the enclosing member's return type, or the lambda's,
    /// with an async body's task already unwrapped. Null where nothing constrains it. Recorded here
    /// because nothing else on the statement says which body it sits in, and a rule judging the value
    /// would otherwise have to rediscover that from the outside.
    /// </summary>
    public IMistType? TargetType { get; } = targetType;

    /// <summary>
    /// Whether the value is returned by reference. Read from the statement's own <c>ref</c> rather
    /// than from the enclosing method's, so what emits is what the author wrote -- a disagreement
    /// between the two is a program to reject, not one to silently repair.
    /// </summary>
    public bool IsRef { get; } = isRef;
}