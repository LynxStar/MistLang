using Mist.ProgramModel.Contracts.Types;
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
    IMistType type) : Expression(node)
{
    /// <summary>
    /// The expression being invoked (a method reference, member access, or delegate value).
    /// </summary>
    public Expression Receiver { get; } = receiver;

    /// <summary>
    /// The bound argument expressions, in source order.
    /// </summary>
    public IReadOnlyList<Expression> Arguments { get; } = arguments;

    public override IMistType Type { get; } = type;
}