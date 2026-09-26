using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
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
    IReadOnlyList<IMistType>? typeArguments = null,
    bool isConditional = false,
    LookupResult? member = null,
    bool isFailed = false) : Expression(node)
{
    /// <summary>
    /// The expression whose member is being accessed.
    /// </summary>
    public Expression Receiver { get; } = receiver;

    /// <summary>
    /// Whether the access is conditional (<c>receiver?.member</c>), short-circuiting to
    /// absence when the receiver is absent. The <see cref="Type"/> is then already lifted.
    /// </summary>
    public bool IsConditional { get; } = isConditional;

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

    /// <summary>
    /// What the name resolved to -- a field, a property, an enum variant -- with the type it was found on.
    /// Recorded because the expression otherwise keeps only the NAME, and a rule judging the access, as
    /// accessibility does, would have to resolve it a second time and could disagree with the binder.
    /// Null where the access resolved no member of its own: a spread element, a callee.
    /// </summary>
    public LookupResult? Member { get; } = member;

    /// <summary>
    /// The member did not resolve and the binder said so, which makes any complaint about this value a
    /// second complaint about one mistake -- see
    /// <see cref="Construction.Binding.PriorFailure"/>. Carried rather than re-derived from
    /// <see cref="Member"/>, because the same unresolved result is a spoken refusal on a type that
    /// answers members and a silence on one that does not, and reading it as a refusal where nothing was
    /// said would swallow the only diagnostic an unknown receiver gets.
    /// </summary>
    public bool IsFailed { get; } = isFailed;
}