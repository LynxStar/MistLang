using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

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
    /// <remarks>
    /// No namespace, because a type parameter is not reachable through one -- it is nameable only
    /// inside the declaration that introduced it, so its qualified name hangs off that declaration.
    /// </remarks>
    public TypeParameter(
        string name,
        int ordinal,
        Scope declaringScope,
        MistType? declaringType)
        : base(name, declaringScope, declaringType)
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
    /// True when this type parameter was declared with <c>params</c>, indicating it
    /// holds a <c>Type[]</c> and can be spread with <c>...</c> at use sites.
    /// </summary>
    public bool HasParams { get; internal set; }

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