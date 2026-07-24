using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A reference to a method (resolved from an identifier or member access). The
/// <see cref="Type"/> is the method's return type. Used as the receiver of an
/// <see cref="InvocationExpression"/>.
/// </summary>
internal sealed class MethodReferenceExpression(
    SyntaxNode node,
    Method method) : Expression(node)
{
    /// <summary>
    /// The resolved method being referenced.
    /// </summary>
    public Method Method { get; } = method;

    public override IMistType Type { get; } = method.ReturnType;
}