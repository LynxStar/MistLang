using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// Every overload a name resolved to, held between the lookup that found them and the context that
/// constrains them to one. A binder intermediate rather than anything in the finished program.
/// <para>
/// Two constraints consume it: an invocation selects on its bound argument types, and a conversion to
/// a function type selects on the target signature. The second arrives either from the position the
/// group sits in or, where an argument's own call has yet to choose, from the parameter that call
/// settles on -- see <see cref="Construction.Binding.MethodGroupConversion"/>. Nothing else consumes
/// one, so a group still standing at the end of binding is two failures rather than a representable
/// state -- nothing resolved it, and nothing reported that.
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
        ConstructedType? receiverConstructed,
        bool isConditional = false)
        : base(node)
    {
        Receiver = receiver;
        MemberName = memberName;
        Candidates = candidates;
        ReceiverConstructed = receiverConstructed;
        IsConditional = isConditional;
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

    /// <summary>
    /// Per candidate, the constructed bases it was reached through -- what an inherited method of a
    /// generic base needs before its signature means anything, and which the receiver alone cannot
    /// say, since each depth of the chain may bind the parameters differently.
    /// </summary>
    public IReadOnlyDictionary<Method, IReadOnlyList<ConstructedType>>? Paths { get; init; }

    /// <summary>
    /// Whether the group was reached through <c>?.</c>, in which case the invocation that
    /// consumes it lifts its result and emits conditionally.
    /// </summary>
    public bool IsConditional { get; }

    public IReadOnlyList<IMistType>? StatedTypeArguments { get; init; }

    /// <summary>
    /// The stated arguments as each candidate of the written count resolved them, a name being a value
    /// where that candidate has a dependent slot and a type where it does not, with the reports of that
    /// resolution withheld until the call has chosen, so one written list resolved several ways is
    /// reported once: the chosen candidate's, or with none chosen, the first slotted resolution's
    /// unless some resolution took the list cleanly. Null when no candidate has such a slot.
    /// <see cref="StatedShared"/> says whether <see cref="StatedTypeArguments"/> was resolved for a pack
    /// candidate or a spread and reported as it went; otherwise it is the first candidate's own list, kept
    /// for its count.
    /// </summary>
    public IReadOnlyDictionary<Method, StatedResolution>? StatedPerCandidate { get; init; }

    public bool StatedShared { get; init; }

    public IReadOnlyList<IMistType>? StatedArgumentsFor(Method candidate)
    {
        return StatedPerCandidate is { } own && own.TryGetValue(candidate, out var resolved) ? resolved.Arguments : StatedTypeArguments;
    }

    public IReadOnlyList<SlotCapture> StatedCapturesFor(Method candidate)
    {
        return StatedPerCandidate is { } own && own.TryGetValue(candidate, out var resolved) ? resolved.Captures : [];
    }

    public void SettleStated(Method? selected)
    {
        if(StatedPerCandidate is null)
        {
            return;
        }

        var resolutions = StatedPerCandidate.Values.Distinct(ReferenceEqualityComparer.Instance).Cast<StatedResolution>().ToList();
        var kept = selected is not null
            ? StatedPerCandidate.GetValueOrDefault(selected)
            : StatedShared || resolutions.Exists(resolution => resolution.Withheld is { Occurrences.IsEmpty: true }) ? null : resolutions[0];

        foreach(var resolution in resolutions)
        {
            if(resolution.Withheld is not { Decided: false } withheld)
            {
                continue;
            }

            if(ReferenceEquals(resolution, kept))
            {
                withheld.Commit();
            }
            else
            {
                withheld.Discard();
            }
        }
    }

    public override IMistType Type { get; } = MethodGroupType.Instance;
}