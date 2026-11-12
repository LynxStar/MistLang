using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A reference to a method, used as the receiver of an <see cref="InvocationExpression"/>.
/// </summary>
internal sealed class MethodReferenceExpression(
    SyntaxNode node,
    Method method,
    IReadOnlyList<IMistType>? typeArguments = null) : Expression(node)
{
    public Method Method { get; } = method;

    public IReadOnlyList<IMistType> TypeArguments { get; } = typeArguments ?? [];

    public override IMistType Type { get; } = method.ReturnType;
}