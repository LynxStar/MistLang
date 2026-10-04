using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// A property declared on a type.
/// </summary>
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

    /// <summary>
    /// The attributes applied to this property.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    /// <summary>
    /// The getter accessor, or null if the property has no getter.
    /// </summary>
    public PropertyAccessor? Getter { get; init; }

    /// <summary>
    /// The setter accessor, or null if the property has no setter.
    /// </summary>
    public PropertyAccessor? Setter { get; init; }

    public bool UsesBackingField { get; internal set; }

    public bool HasBackingStorage => UsesBackingField || Getter is { IsAuto: true } || Setter is { IsAuto: true };

    /// <summary>
    /// The property initializer expression, or null if none. Settable for the same reason
    /// <see cref="Field.Initializer"/> is: Phase 2 parks the unbound expression, Phase 3 replaces it.
    /// </summary>
    public Expression? Initializer { get; internal set; }

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}