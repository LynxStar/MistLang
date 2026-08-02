using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A member access expression (<c>receiver.memberName</c>). The <see cref="Type"/>
/// is the type of the accessed member (field type, property type, or method return type).
/// </summary>
internal sealed class MemberAccessExpression(
    SyntaxNode node,
    Expression receiver,
    string memberName,
    IMistType type,
    IReadOnlyList<IMistType>? typeArguments = null) : Expression(node)
{
    /// <summary>
    /// The expression whose member is being accessed.
    /// </summary>
    public Expression Receiver { get; } = receiver;

    /// <summary>
    /// The name of the member being accessed.
    /// </summary>
    public string MemberName { get; } = memberName;

    /// <summary>
    /// The called method's OWN type arguments, never the receiver's, which <see cref="Receiver"/>
    /// already carries. Empty for anything that is not a call to a generic method.
    /// </summary>
    public IReadOnlyList<IMistType> TypeArguments { get; } = typeArguments ?? [];

    public override IMistType Type { get; } = type;
}