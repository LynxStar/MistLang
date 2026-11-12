using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Members;

internal sealed class Property(
    string name,
    IMistType type,
    MemberModifiers modifiers) : IProperty, IHasAttributes
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    public MemberModifiers Modifiers { get; } = modifiers;

    public MemberType? DeclaringType { get; init; }

    public Storm.Runtime.SyntaxNode? DeclarationNode { get; init; }

    public TypeParameter? ProjectedParameter { get; init; }

    public NativeSlotKind SlotKind { get; init; }

    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    public PropertyAccessor? Getter { get; init; }

    public PropertyAccessor? Setter { get; init; }

    public bool UsesBackingField { get; internal set; }

    public bool HasBackingStorage => UsesBackingField || Getter is { IsAuto: true } || Setter is { IsAuto: true };

    /// <summary>
    /// Settable for the same reason <see cref="Field.Initializer"/> is.
    /// </summary>
    public Expression? Initializer { get; internal set; }

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}