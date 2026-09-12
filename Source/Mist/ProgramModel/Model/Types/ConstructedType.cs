using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A constructed (closed) generic type — a resolved type definition with concrete type arguments.
/// Not registered in a namespace — constructed on-the-fly during type resolution.
/// <para>
/// A NESTED type reached through an instantiation of its enclosing generic is a construction too,
/// of the nested definition with no arguments of its own and the instantiation as
/// <see cref="Enclosing"/>: <c>Box&lt;int&gt;.Wrapper</c> is a different type from
/// <c>Box&lt;string&gt;.Wrapper</c>, and its members are substituted through <c>Box&lt;int&gt;</c>. The
/// only construction with no arguments is this one.
/// </para>
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

    /// <summary>
    /// The unparameterized type definition.
    /// </summary>
    public IMistType Definition { get; }

    /// <summary>
    /// The resolved type arguments.
    /// </summary>
    public IReadOnlyList<IMistType> TypeArguments { get; }

    public ConstructedType? Enclosing { get; }

    IMistType IConstructedType.Definition => Definition;

    IReadOnlyList<IMistType> IConstructedType.TypeArguments => TypeArguments;

    IConstructedType? IConstructedType.Enclosing => Enclosing;

    /// <summary>
    /// The definition's name with the arguments in place of its arity marker, so
    /// <c>Box&lt;&gt;</c> constructed with <c>int</c> is named <c>Box&lt;int&gt;</c>. Stripping is
    /// required rather than cosmetic: the definition is REGISTERED under the arity-bearing name, so
    /// interpolating it directly yields <c>Box&lt;&gt;&lt;int&gt;</c>.
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
    /// Qualified by the definition's namespace, so <c>List&lt;int&gt;</c> constructed from two
    /// different namespaces' <c>List&lt;&gt;</c> are distinguishable -- which matters where a
    /// fully qualified name identifies a type, as it does when telling overloads apart.
    /// <para>
    /// A definition that is not a declared type has no namespace to contribute, which is only
    /// reachable through a synthetic construction.
    /// </para>
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