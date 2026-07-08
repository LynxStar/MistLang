using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A type parameter declared on a generic type. This is itself a type — it can
/// appear in base clauses, member signatures, and anywhere a type reference resolves.
/// Lives in its declaring type's scope; does not participate in namespace registration.
/// </summary>
internal sealed class TypeParameter : MistType, ITypeParameter
{
    public TypeParameter(
        string name,
        int ordinal,
        Scope declaringScope,
        MistType declaringType)
        : base(name, declaringType.NamespaceName, declaringScope, declaringType)
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