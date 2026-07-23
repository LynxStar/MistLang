using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// A spread-generated member family (Requirement 10) recorded on a variadic type in the
/// classic semantic model, produced when a spread of a type-parameter pack appears in a
/// member-declaring position -- the field spread <c>public TArgs... Item;</c> or the
/// property spread <c>public TArgs... Slot { get; }</c>.
///
/// <para>
/// The family surfaces an aggregate member named by the <see cref="Stem"/> whose type is
/// the value-pack tuple <see cref="AggregateType"/> (<c>(TArgs...)</c>, the whole
/// ValueTuple), together with the 1-based element projections <c>Stem1 .. StemN</c> where
/// <c>N</c> is the bound pack length: each projection <c>Stemk</c> has the pack's element
/// type at 0-based index <c>k - 1</c> and denotes the k-th element of the aggregate
/// (<see cref="MaterializeProjections"/>). The aggregate and every projection share the
/// accessibility and member kind (<see cref="Kind"/>) of the spread declaration. The
/// aggregate itself is the real <see cref="AggregateMember"/> symbol (a field or property
/// named by the stem) in the declaring type's member scope; this record captures the
/// family so the projections can be produced.
/// </para>
///
/// <para>
/// While the <see cref="Pack"/> is symbolic -- unbound, with unknown length -- the <c>N</c>
/// projections cannot be enumerated, so the family is recorded structurally and per-arity
/// projection materialization is deferred to the point where the pack binds a concrete
/// <c>Type[]</c> (backend monomorphization). The aggregate <see cref="AggregateType"/>
/// carries the pack as its single symbolic element. It follows that an empty pack yields
/// the empty aggregate and zero projections (Requirement 10.6); a length-1 pack yields
/// <c>Stem</c> of type <c>(T)</c> and the single projection <c>Stem1</c>
/// (Requirement 10.7); and two spreads of the same pack correlate at the same <c>N</c>
/// because they share <see cref="Pack"/> (Requirement 10.8). This mirrors the ProgramModel
/// track's <c>SpreadMember</c>, expressed over the legacy <c>Mist.Semantics</c> model.
/// </para>
/// </summary>
public sealed class SemanticSpreadMember
{
    public SemanticSpreadMember(
        string stem,
        TypeParameterType pack,
        TupleType aggregateType,
        SpreadMemberKind kind,
        ISymbol aggregateMember)
    {
        Stem = stem;
        Pack = pack;
        AggregateType = aggregateType;
        Kind = kind;
        AggregateMember = aggregateMember;
    }

    /// <summary>
    /// The base identifier of the declaration (for example <c>Item</c> in
    /// <c>public TArgs... Item;</c>). Also the name of the aggregate member and the prefix
    /// of every projection name <c>Stem1 .. StemN</c>. Guaranteed non-empty (an empty stem
    /// is rejected with <c>MIST_VAR_0008</c> before a family is recorded).
    /// </summary>
    public string Stem { get; }

    /// <summary>
    /// The type-parameter pack spread by this member. Its bound length determines the
    /// number of element projections; two families over the same pack instance correlate
    /// at the same length.
    /// </summary>
    public TypeParameterType Pack { get; }

    /// <summary>
    /// The aggregate member type <c>(TArgs...)</c> -- the whole value-pack tuple. While the
    /// pack is symbolic this carries the pack as its single element; the projections follow
    /// the tuple's elements once the pack binds a concrete <c>Type[]</c>.
    /// </summary>
    public TupleType AggregateType { get; }

    /// <summary>Whether the family surfaces as fields or properties.</summary>
    public SpreadMemberKind Kind { get; }

    /// <summary>
    /// The real aggregate member symbol named by the <see cref="Stem"/> -- a field or
    /// property in the declaring type's member scope. Its resolved
    /// <see cref="ISymbol.Accessibility"/> is the accessibility shared by every element
    /// projection.
    /// </summary>
    public ISymbol AggregateMember { get; }

    /// <summary>
    /// Materializes the 1-based element projections for a concrete bound pack
    /// (Requirements 10.1, 10.6, 10.7). Given the pack's bound element types in order,
    /// returns one <c>(Name, Type)</c> pair per element where projection <c>k</c> (1-based)
    /// is named <c>Stem{k}</c> and has the element type at 0-based index <c>k - 1</c>. An
    /// empty binding yields zero projections; a length-1 binding yields the single
    /// projection <c>Stem1</c> (the numeric suffix is always present). This is the pure
    /// projection rule the type checker and backend apply once the pack binds a concrete
    /// <c>Type[]</c>; it does not itself perform binding.
    /// </summary>
    public IReadOnlyList<(string Name, IType Type)> MaterializeProjections(IReadOnlyList<IType> boundElementTypes)
    {
        var projections = new List<(string Name, IType Type)>(boundElementTypes.Count);
        for(var k = 0; k < boundElementTypes.Count; k++)
        {
            projections.Add((ProjectionName(k + 1), boundElementTypes[k]));
        }

        return projections;
    }

    /// <summary>
    /// The projection name for a 1-based element index: the stem followed by the index (for
    /// example <c>Item1</c>, <c>Item2</c>). Matches ValueTuple's 1-based positional naming.
    /// </summary>
    public string ProjectionName(int oneBasedIndex)
    {
        return $"{Stem}{oneBasedIndex}";
    }
}