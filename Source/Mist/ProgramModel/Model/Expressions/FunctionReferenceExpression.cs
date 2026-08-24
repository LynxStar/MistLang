using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A method named as a VALUE rather than called: the result of converting a method group against a
/// function type. Distinct from <see cref="MethodReferenceExpression"/>, whose type is the method's
/// RESULT because it exists to be the receiver of an invocation -- here the whole function is the value,
/// so the type is the function type it converted to.
/// </summary>
internal sealed class FunctionReferenceExpression(
    SyntaxNode node,
    Expression? receiver,
    Method method,
    IMistType type) : Expression(node)
{
    /// <summary>
    /// What the method was reached through, or null for a bare name.
    /// </summary>
    public Expression? Receiver { get; } = receiver;

    public Method Method { get; } = method;

    public override IMistType Type { get; } = type;
}