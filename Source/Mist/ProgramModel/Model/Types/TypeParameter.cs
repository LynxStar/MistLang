using System.Collections.Concurrent;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
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
    /// How this parameter's type arguments are supplied. The two spellings differ in that and in
    /// nothing else: <c>params TArgs</c> and <c>Type[]:TArgs</c> both declare a slot holding a
    /// <c>Type[]</c>, spreadable with <c>...</c>.
    /// </summary>
    public TypePackKind PackKind { get; internal set; }

    public bool IsPack => PackKind != TypePackKind.None;

    /// <summary>
    /// The arity a fixed-size domain pins (<c>Type[3]:TArgs</c>), null where the domain is
    /// <c>Type[]</c> and the use site decides.
    /// </summary>
    public int? PackLength { get; internal set; }

    private ConcurrentDictionary<int, PackElementType>? elements;

    /// <summary>
    /// The element of this pack at <paramref name="index"/>. One object per index, since a type
    /// parameter is compared by reference and two mentions of <c>TArgs[0]</c> are one type.
    /// </summary>
    public PackElementType ElementAt(int index)
    {
        return LazyInitializer.EnsureInitialized(ref elements)
            .GetOrAdd(index, static (element, pack) => new PackElementType(pack, element), this);
    }

    /// <summary>
    /// What the declaration annotated the domain with, carried from construction to resolution
    /// because <see cref="Domain"/> cannot be resolved where the parameter is created -- Standard
    /// is not registered yet.
    /// <para>
    /// A descriptor and a name rather than the annotation's syntax, which is what keeps the AST out
    /// of the model here. The annotation admits four shapes and three of them carry no syntax worth
    /// deferring: a primitive keyword maps to a well-known type by a pure switch, a pack's domain
    /// is built from <see cref="PackKind"/> and <see cref="PackLength"/>, and a plain parameter's
    /// is constant. Only a named refinement needs anything, and what it needs is one identifier.
    /// </para>
    /// </summary>
    public WellKnownType? DomainPrimitive { get; internal set; }

    /// <inheritdoc cref="DomainPrimitive"/>
    public string? DomainTypeName { get; internal set; }

    /// <summary>
    /// True when this parameter was written with a value domain (<c>Size:int</c>) rather than as a
    /// plain type parameter. The SYNTACTIC fact only: <see cref="Domain"/> is what will carry the
    /// resolved domain type, and nothing populates it yet, so a consumer that needs to know WHICH
    /// domain still cannot ask. Recorded because a backend has to refuse what it cannot spell, and
    /// telling the two kinds apart is the whole of that question.
    /// </summary>
    public bool IsDependent { get; internal set; }

    /// <summary>
    /// The constraints declared on this type parameter via where clauses.
    /// Populated during Phase 2 when constraints are resolved.
    /// </summary>
    public virtual List<TypeParameterConstraint> ConstraintsList { get; } = [];

    /// <summary>
    /// The adhoc refinement predicate AST node for this type parameter, if any
    /// (from a <c>where Size >= 0</c> clause on a dependent type param).
    /// Null until resolved. Interpretation deferred to the pattern resolution phase.
    /// </summary>
    public Storm.Runtime.SyntaxNode? RefinementNode { get; internal set; }

    public Pattern? Refinement { get; internal set; }

    public Property? ValueProperty { get; internal set; }

    IMistType? ITypeParameter.Domain => Domain;

    IReadOnlyList<ITypeParameterConstraint> ITypeParameter.Constraints => ConstraintsList;
}