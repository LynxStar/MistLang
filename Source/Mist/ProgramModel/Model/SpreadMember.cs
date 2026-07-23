using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A spread-generated member family (Requirement 10) produced when a spread of a
/// type-parameter pack appears in a member-declaring position, for example the field
/// spread <c>public TArgs... Item;</c> or the property spread
/// <c>public TArgs... Slot { get; }</c>.
///
/// <para>
/// The family surfaces an aggregate member named by the <see cref="Stem"/> whose type is
/// the value-pack tuple <see cref="AggregateType"/> (<c>(TArgs...)</c>, the whole
/// ValueTuple), together with the 1-based element projections <c>Stem1 .. StemN</c> where
/// <c>N</c> is the bound pack length: each projection <c>Stemk</c> has the pack's element
/// type at 0-based index <c>k - 1</c> and denotes the k-th element of the aggregate. The
/// aggregate and every projection share the same accessibility (<see cref="Modifiers"/>)
/// and member kind (<see cref="Kind"/>). The aggregate itself is materialized eagerly as a
/// real <see cref="Field"/>/<see cref="Property"/> named by the stem in the declaring
/// type's member list; this record captures the family so the projections can be produced.
/// </para>
///
/// <para>
/// While the <see cref="Pack"/> is symbolic — unbound, with unknown length — the <c>N</c>
/// projections cannot be enumerated, so the family is recorded structurally and per-arity
/// projection materialization is deferred to the point where the pack binds a concrete
/// <c>Type[]</c>. This mirrors how the value-pack tuple's positional <c>Item1 .. ItemN</c>
/// fields expand only once the pack binds (see <see cref="TupleType.PositionalFields"/>):
/// the aggregate <see cref="AggregateType"/> carries the pack as its single symbolic
/// element and re-interns into an <c>N</c>-element tuple on binding, so the projections
/// follow the aggregate's positional fields under the stem. It follows that an empty pack
/// yields the empty aggregate and zero projections (Requirement 10.6); a length-1 pack
/// yields <c>Stem</c> of type <c>(T)</c> and the single projection <c>Stem1</c>
/// (Requirement 10.7); and two spreads of the same pack correlate at the same <c>N</c>
/// because they share <see cref="Pack"/> (Requirement 10.8).
/// </para>
/// </summary>
internal sealed class SpreadMember(
    string stem,
    TypeParameter pack,
    TupleType aggregateType,
    SpreadMemberKind kind,
    MemberModifiers modifiers,
    OwnershipKind ownership)
{
    /// <summary>
    /// The base identifier of the declaration (for example <c>Item</c> in
    /// <c>public TArgs... Item;</c>). Also the name of the aggregate member and the prefix
    /// of every projection name <c>Stem1 .. StemN</c>. Guaranteed non-empty (an empty stem
    /// is rejected with <c>MIST_VAR_0008</c> before a family is recorded).
    /// </summary>
    public string Stem { get; } = stem;

    /// <summary>
    /// The type-parameter pack spread by this member. Its bound length determines the
    /// number of element projections; two families over the same pack correlate at the
    /// same length.
    /// </summary>
    public TypeParameter Pack { get; } = pack;

    /// <summary>
    /// The aggregate member type <c>(TArgs...)</c> — the whole value-pack tuple. Its
    /// positional fields become the element projections once the pack binds.
    /// </summary>
    public TupleType AggregateType { get; } = aggregateType;

    /// <summary>Whether the family surfaces as fields or properties.</summary>
    public SpreadMemberKind Kind { get; } = kind;

    /// <summary>
    /// The accessibility and other modifiers applied to the aggregate and every projection.
    /// </summary>
    public MemberModifiers Modifiers { get; } = modifiers;

    /// <summary>
    /// The ownership classification applied to the aggregate and every projection when
    /// <see cref="Kind"/> is <see cref="SpreadMemberKind.Field"/>; unrestricted for a
    /// property family.
    /// </summary>
    public OwnershipKind Ownership { get; } = ownership;
}