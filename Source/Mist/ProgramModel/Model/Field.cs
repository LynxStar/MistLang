using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A field declared on a type. The declaring AST node is not stored here; when a
/// field has an initializer, the AST is reachable through <see cref="Initializer"/>'s
/// <see cref="Expression.Node"/>. Synthetic fields (e.g., tuple elements) simply have
/// no initializer and therefore no associated node.
/// </summary>
internal sealed class Field(
    string name,
    IMistType type,
    OwnershipKind ownership,
    MemberModifiers modifiers) : IField, IHasAttributes
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    public OwnershipKind Ownership { get; } = ownership;

    public MemberModifiers Modifiers { get; } = modifiers;

    /// <summary>
    /// The attributes applied to this field.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    /// <summary>
    /// The field initializer expression, or null if none. Also the sole holder of
    /// the field's AST node (via <see cref="Expression.Node"/>) when present.
    /// </summary>
    public Expression? Initializer { get; init; }

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}