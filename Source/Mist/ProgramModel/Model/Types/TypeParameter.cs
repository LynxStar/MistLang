using System.Collections.Concurrent;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

internal class TypeParameter : MistType, ITypeParameter
{
    /// <remarks>
    /// No namespace: a type parameter is nameable only inside the declaration that introduced it, so
    /// its qualified name hangs off that declaration.
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

    public int Ordinal { get; }

    public Method? DeclaringMethod { get; internal set; }

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
    /// One object per index, since a type parameter is compared by reference and two mentions of
    /// <c>TArgs[0]</c> are one type.
    /// </summary>
    public PackElementType ElementAt(int index)
    {
        return LazyInitializer.EnsureInitialized(ref elements)
            .GetOrAdd(index, static (element, pack) => new PackElementType(pack, element), this);
    }

    /// <summary>
    /// The domain annotation as a descriptor and a name rather than syntax, which keeps the AST out of
    /// the model. Of the annotation's four shapes only a named refinement needs anything deferred, and
    /// that is a qualified name: a primitive keyword maps to a well-known type by a pure switch, a
    /// pack's domain is built from <see cref="PackKind"/> and <see cref="PackLength"/>, and a plain
    /// parameter's is constant.
    /// </summary>
    public WellKnownType? DomainPrimitive { get; internal set; }

    /// <inheritdoc cref="DomainPrimitive"/>
    public string? DomainTypeName { get; internal set; }

    /// <summary>
    /// Written with a value domain (<c>Size:int</c>) rather than as a plain type parameter; which
    /// domain is <see cref="Domain"/>'s.
    /// </summary>
    public bool IsDependent { get; internal set; }

    public virtual List<TypeParameterConstraint> ConstraintsList { get; } = [];

    /// <summary>
    /// The predicate of a <c>where Size >= 0</c> clause on a dependent parameter, held as syntax until
    /// the pattern phase binds it into <see cref="Refinement"/>.
    /// </summary>
    public Storm.Runtime.SyntaxNode? RefinementNode { get; internal set; }

    public Pattern? Refinement { get; internal set; }

    public Property? ValueProperty { get; internal set; }

    IMistType? ITypeParameter.Domain => Domain;

    IReadOnlyList<ITypeParameterConstraint> ITypeParameter.Constraints => ConstraintsList;
}