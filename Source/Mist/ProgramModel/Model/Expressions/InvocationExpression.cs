using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An invocation expression: calling a method, delegate, or func value.
/// The <see cref="Type"/> is the return type of the call.
/// </summary>
internal sealed class InvocationExpression(
    SyntaxNode node,
    Expression receiver,
    IReadOnlyList<Expression> arguments,
    IMistType type,
    Method? method,
    IReadOnlyList<IMistType>? parameterTypes) : Expression(node)
{
    /// <summary>
    /// The expression being invoked (a method reference, member access, or delegate value).
    /// </summary>
    public Expression Receiver { get; } = receiver;

    /// <summary>
    /// The bound argument expressions, in source order.
    /// </summary>
    public IReadOnlyList<Expression> Arguments { get; } = arguments;

    /// <summary>
    /// Which overload the arguments selected. Null when the callee was not a method group at all --
    /// invoking a delegate or func value resolves no member.
    /// <para>
    /// Nothing under Source/ reads this. It is recorded because emission re-writes the call as source
    /// spelled it and lets Roslyn resolve it again, so without it no test could tell a correct
    /// selection from no selection followed by a lucky one.
    /// </para>
    /// </summary>
    public Method? Method { get; } = method;

    /// <summary>
    /// The type each argument position was bound against, read through the call's own substitution:
    /// the selected overload's parameters with its type arguments applied, or a function value's
    /// parameter types. Null when the callee named nothing to read them off. Recorded for the reason
    /// <see cref="Method"/> is, and because the substitution that produced them is not.
    /// </summary>
    public IReadOnlyList<IMistType>? ParameterTypes { get; } = parameterTypes;

    /// <summary>
    /// Whether the binder supplied this call rather than the author writing it: the Drop a sync
    /// context inserts on a task-returning call, which an explicit <c>await</c> or <c>async</c> undoes.
    /// </summary>
    public bool IsImplicit { get; init; }

    public override IMistType Type { get; } = type;
}