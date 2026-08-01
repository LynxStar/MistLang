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
    Method? method) : Expression(node)
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

    public override IMistType Type { get; } = type;
}