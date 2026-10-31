using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// An indexer declared on a type.
/// </summary>
internal sealed class Indexer(
    IMistType type,
    List<Parameter> parameters,
    MemberModifiers modifiers) : IIndexer, IHasAttributes, IOverload
{
    public IMistType Type { get; } = type;

    public List<Parameter> ParametersList { get; } = parameters;

    public MemberModifiers Modifiers { get; } = modifiers;

    public MemberType? DeclaringType { get; init; }

    public Storm.Runtime.SyntaxNode? DeclarationNode { get; init; }

    public NativeSlotKind SlotKind { get; init; }

    /// <summary>
    /// The attributes applied to this indexer.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    /// <summary>
    /// The getter accessor, or null if the indexer has no getter.
    /// </summary>
    public PropertyAccessor? Getter { get; init; }

    /// <summary>
    /// The setter accessor, or null if the indexer has no setter.
    /// </summary>
    public PropertyAccessor? Setter { get; init; }

    List<TypeParameter> IOverload.TypeParametersList => [];
    IReadOnlyList<IParameter> IIndexer.Parameters => ParametersList;
    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}