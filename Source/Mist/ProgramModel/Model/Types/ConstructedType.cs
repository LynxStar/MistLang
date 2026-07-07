using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A constructed (closed) generic type — a resolved type definition with concrete type arguments.
/// Not registered in a namespace — constructed on-the-fly during type resolution.
/// </summary>
internal sealed class ConstructedType : MistType, IConstructedType
{
    public ConstructedType(IMistType definition, IReadOnlyList<IMistType> typeArguments)
        : base(BuildName(definition, typeArguments), definition.NamespaceName, new Scope(null), declaringType: null)
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

    private static string BuildName(IMistType definition, IReadOnlyList<IMistType> typeArguments)
    {
        return $"{definition.Name}<{string.Join(", ", typeArguments.Select(a => a.Name))}>";
    }
}