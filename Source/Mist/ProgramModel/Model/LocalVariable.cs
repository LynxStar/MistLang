using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A local or parameter introduced within a body.
/// </summary>
internal sealed class LocalVariable(string name, IMistType type)
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    public bool IsKnownResultError { get; init; }

    public bool IsDiscard { get; init; }

    public bool InfersOwnership { get; init; }

    public bool IsRef { get; init; }

    /// <summary>
    /// The tier the binding was declared at. Every tier erases at emission, but <c>owned</c> is read
    /// before that: it is what obliges an incoming value to be independent, and so decides whether a
    /// clone is inserted. See <see cref="Construction.Binding.OwnedProvenance"/>.
    /// </summary>
    public OwnershipKind Ownership { get; init; }

    /// <summary>
    /// The <c>readonly</c> modifier as written. The backend reads it only PAIRED with
    /// <see cref="IsRef"/>, which is Mist's <c>readonly ref</c> -- an alias the referent cannot be
    /// written through. On a non-ref local it is recorded and unread, since what a readonly binding
    /// then forbids is readonly-locals-and-blocks' question rather than this one's.
    /// </summary>
    public bool IsReadonly { get; init; }

    /// <summary>
    /// The binding took its type from a value that already failed and was reported, so a complaint about
    /// a use of it would repeat that mistake. POSITIVE rather than marking the unfinished case: a site
    /// that never sets it leaves a second complaint, where one that forgot to mark an unfinished value
    /// would silence a construct nothing has reported.
    /// </summary>
    public bool IsFailed { get; init; }

    public Expression? FailureSource { get; init; }

    public Parameter? Parameter { get; init; }

    /// <summary>
    /// The name stands for an ambient value, a <c>with</c> clause's or a named <c>with</c> declaration's.
    /// Recorded because nothing else tells a clause's local from an ordinary one, and structured-context
    /// Requirements 4.1 and 7.5 make such a name unassignable: a second value under it would no longer
    /// be the one a callee reads.
    /// </summary>
    public bool IsContextBound { get; init; }

    /// <summary>
    /// The pack a <c>TArgs is Type[N] fixed</c> guard proved the size of, for the binding that guard
    /// declares. Kept because the binding's own type, <c>Type[N]</c>, says how many elements there are
    /// and not whose: <c>fixed[0]</c> in a type position has to BE <c>TArgs[0]</c>, the type every other
    /// mention of that element names, or a value read out of the pack would not be assignable to it.
    /// </summary>
    public TypeParameter? NarrowedPack { get; init; }
}