using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A constructed (closed) generic type — a resolved type definition with concrete type arguments.
/// Not registered in a namespace — constructed on-the-fly during type resolution.
/// </summary>
internal sealed class ConstructedType : MistType, IConstructedType
{
    public ConstructedType(IMistType definition, IReadOnlyList<IMistType> typeArguments)
        : base(
            BuildName(definition, typeArguments),
            Qualify(definition, typeArguments),
            new Scope(null),
            declaringType: null)
    {
        Definition = definition;
        TypeArguments = typeArguments;
    }

    /// <summary>
    /// The unparameterized type definition.
    /// </summary>
    public IMistType Definition { get; }

    /// <summary>
    /// The resolved type arguments.
    /// </summary>
    public IReadOnlyList<IMistType> TypeArguments { get; }

    IMistType IConstructedType.Definition => Definition;

    IReadOnlyList<IMistType> IConstructedType.TypeArguments => TypeArguments;

    /// <summary>
    /// Resolves a type parameter declared on this constructed type's definition to the
    /// concrete type argument at the same ordinal position. Returns the type parameter
    /// unchanged if it does not belong to this definition or is out of range.
    /// </summary>
    public IMistType Substitute(IMistType type)
    {
        if(type is TypeParameter tp && tp.Ordinal < TypeArguments.Count)
        {
            return TypeArguments[tp.Ordinal];
        }

        return type;
    }

    private static string BuildName(IMistType definition, IReadOnlyList<IMistType> typeArguments)
    {
        return $"{definition.Name}<{string.Join(", ", typeArguments.Select(a => a.Name))}>";
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
    private static string Qualify(IMistType definition, IReadOnlyList<IMistType> typeArguments)
    {
        var name = BuildName(definition, typeArguments);

        return definition is DeclaredType { NamespaceName: { Length: > 0 } namespaceName }
            ? $"{namespaceName}.{name}"
            : name
            ;
    }
}