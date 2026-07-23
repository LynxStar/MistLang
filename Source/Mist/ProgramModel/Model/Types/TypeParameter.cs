using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A type parameter declared on a generic type. This is itself a type — it can
/// appear in base clauses, member signatures, and anywhere a type reference resolves.
/// Lives in its declaring type's scope; does not participate in namespace registration.
///
/// A regular type parameter is closed by supplying a type argument at the use site.
/// The <see cref="TypeSlot"/> subclass is a more specific kind that is closed by
/// conformance instead — and, unlike a plain type parameter, may declare its own
/// type parameters.
/// </summary>
internal class TypeParameter : MistType, ITypeParameter
{
    public TypeParameter(
        string name,
        int ordinal,
        Scope declaringScope,
        MistType? declaringType)
        : base(name, declaringType?.NamespaceName ?? declaringScope.Namespace ?? "", declaringScope, declaringType)
    {
        Ordinal = ordinal;
    }

    /// <summary>
    /// The zero-based positional index within the declaring type's parameter list.
    /// </summary>
    public int Ordinal { get; }

    /// <summary>
    /// The domain of this type parameter — the type whose values can fill this slot.
    /// For regular type parameters (<c>T</c>), this resolves to <c>Standard.Type</c>.
    /// For dependent type parameters (<c>N:int</c>), this resolves to the annotated
    /// value type (e.g., <c>Standard.Int32</c>).
    /// Null until resolved in Phase 2.
    /// </summary>
    public MistType? Domain { get; internal set; }

    /// <summary>
    /// How this type parameter participates as a type-parameter pack, or
    /// <see cref="TypeParameterPackKind.None"/> when it is an ordinary
    /// (regular or dependent) type parameter. A pack is sugar for a dependent
    /// type parameter whose value domain is the comptime metatype array
    /// <c>Type[]</c> (unknown length) or <c>Type[N]</c> (fixed length — see
    /// <see cref="FixedPackLength"/>).
    /// </summary>
    public TypeParameterPackKind PackKind { get; internal set; } = TypeParameterPackKind.None;

    /// <summary>
    /// True when this type parameter is a pack (its <see cref="PackKind"/> is not
    /// <see cref="TypeParameterPackKind.None"/>).
    /// </summary>
    public bool IsPack => PackKind != TypeParameterPackKind.None;

    /// <summary>
    /// True when this pack is the list's single unbracketed (<c>params</c>) slot,
    /// constructed bracket-free at the use site. False for a bracketed pack or a
    /// non-pack parameter.
    /// </summary>
    public bool IsUnbracketedPack => PackKind == TypeParameterPackKind.Unbracketed;

    /// <summary>
    /// For a fixed-size pack whose domain is <c>Type[N]</c>, the comptime length
    /// <c>N</c>. Null for an unknown-length <c>Type[]</c> pack (and for a non-pack
    /// parameter).
    /// </summary>
    public long? FixedPackLength { get; internal set; }

    /// <summary>
    /// The constraints declared on this type parameter via where clauses.
    /// Populated during Phase 2 when constraints are resolved.
    /// </summary>
    public List<TypeParameterConstraint> ConstraintsList { get; } = [];

    /// <summary>
    /// The adhoc refinement predicate AST node for this type parameter, if any
    /// (from a <c>where Size >= 0</c> clause on a dependent type param).
    /// Null until resolved. Interpretation deferred to the pattern resolution phase.
    /// </summary>
    public Storm.Runtime.SyntaxNode? RefinementNode { get; internal set; }

    IMistType? ITypeParameter.Domain => Domain;

    IReadOnlyList<ITypeParameterConstraint> ITypeParameter.Constraints => ConstraintsList;
}