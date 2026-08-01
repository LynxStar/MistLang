using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// Every overload a name resolved to, held between the lookup that found them and the context that
/// constrains them to one. A binder intermediate rather than anything in the finished program.
/// <para>
/// Exactly two contexts consume it, each supplying a different constraint: an invocation selects on
/// its bound argument types, and a conversion to a function type selects on the target signature.
/// There is no third, so a group still standing at the end of binding is two failures rather than a
/// representable state -- nothing resolved it, and nothing reported that.
/// </para>
/// <para>
/// It has to exist because arguments are bound before the overload they are arguments to is
/// selected, so a group in an argument slot has no target available yet. Reaching its consumer
/// directly does not need it -- <c>BindMemberAccess</c> and <c>BindInvocation</c> are one statement
/// apart -- and there it is only ever a local.
/// </para>
/// </summary>
internal sealed class MethodGroupExpression : Expression
{
    public MethodGroupExpression(
        SyntaxNode node,
        Expression? receiver,
        string memberName,
        IReadOnlyList<Method> candidates,
        ConstructedType? receiverConstructed)
        : base(node)
    {
        Receiver = receiver;
        MemberName = memberName;
        Candidates = candidates;
        ReceiverConstructed = receiverConstructed;
    }

    /// <summary>
    /// What the group was reached through, or null for a bare name resolved from the scope chain.
    /// </summary>
    public Expression? Receiver { get; }

    public string MemberName { get; }

    /// <summary>Never empty.</summary>
    public IReadOnlyList<Method> Candidates { get; }

    /// <summary>
    /// The receiver's constructed type, when it had one. Selection needs it to substitute -- see
    /// <c>OverloadResolution.ParameterType</c>.
    /// </summary>
    public ConstructedType? ReceiverConstructed { get; }

    public override IMistType Type { get; } = MethodGroupType.Instance;
}