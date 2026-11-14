using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Members;

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

    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    public PropertyAccessor? Getter { get; init; }

    public PropertyAccessor? Setter { get; init; }

    List<TypeParameter> IOverload.TypeParametersList => [];
    IReadOnlyList<IParameter> IIndexer.Parameters => ParametersList;
    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}