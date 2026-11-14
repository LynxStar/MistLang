using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Not registered in a namespace; constructed during type resolution. A NESTED type reached through an
/// instantiation of its enclosing generic is a construction too, of the nested definition with no
/// arguments of its own and the instantiation as <see cref="Enclosing"/>, so it is a different type per
/// instantiation and its members are substituted through that instantiation. That is the only
/// construction with no arguments.
/// </summary>
internal sealed class ConstructedType : MistType, IConstructedType
{
    public ConstructedType(
        IMistType definition, IReadOnlyList<IMistType> typeArguments, ConstructedType? enclosing = null)
        : base(
            BuildName(definition, typeArguments, enclosing),
            Qualify(definition, typeArguments, enclosing),
            new Scope(null),
            declaringType: null)
    {
        Definition = definition;
        TypeArguments = typeArguments;
        Enclosing = enclosing;
    }

    public IMistType Definition { get; }

    public IReadOnlyList<IMistType> TypeArguments { get; }

    public ConstructedType? Enclosing { get; }

    IMistType IConstructedType.Definition => Definition;

    IReadOnlyList<IMistType> IConstructedType.TypeArguments => TypeArguments;

    IConstructedType? IConstructedType.Enclosing => Enclosing;

    /// <summary>
    /// Stripping the arity marker is required rather than cosmetic: the definition is REGISTERED under
    /// the arity-bearing name, so interpolating it directly yields a second argument list.
    /// </summary>
    private static string BuildName(
        IMistType definition, IReadOnlyList<IMistType> typeArguments, ConstructedType? enclosing)
    {
        var segment = Segment(definition, typeArguments);

        return enclosing is null ? segment : $"{enclosing.Name}.{segment}";
    }

    private static string Segment(IMistType definition, IReadOnlyList<IMistType> typeArguments)
    {
        var name = StripArity(definition.Name);

        return typeArguments.Count == 0
            ? name
            : $"{name}<{string.Join(", ", typeArguments.Select(a => a.Name))}>"
            ;
    }

    /// <summary>
    /// Qualified by the definition's namespace, so constructions of same-named definitions from two
    /// namespaces stay distinct where a fully qualified name identifies a type, as when telling
    /// overloads apart. Only a synthetic construction has a definition that is not a declared type.
    /// </summary>
    private static string Qualify(
        IMistType definition, IReadOnlyList<IMistType> typeArguments, ConstructedType? enclosing)
    {
        if(enclosing is not null)
        {
            return $"{enclosing.FullyQualifiedName}.{Segment(definition, typeArguments)}";
        }

        var name = BuildName(definition, typeArguments, enclosing: null);

        return definition is DeclaredType { NamespaceName: { Length: > 0 } namespaceName }
            ? $"{namespaceName}.{name}"
            : name
            ;
    }
}