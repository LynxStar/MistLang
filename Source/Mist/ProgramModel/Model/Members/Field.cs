using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Members;

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

    public MemberType? DeclaringType { get; init; }

    public Storm.Runtime.SyntaxNode? DeclarationNode { get; init; }

    /// <summary>
    /// The attributes applied to this field.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    /// <summary>
    /// The field initializer expression, or null if none. Also the sole holder of
    /// the field's AST node (via <see cref="Expression.Node"/>) when present.
    /// <para>
    /// Settable for the same reason <see cref="Statements.Block.Statements"/> is: signature
    /// resolution runs before any body binding, so Phase 2 parks the unbound expression here and
    /// Phase 3 replaces it with the resolved one. Nothing reads it in between.
    /// </para>
    /// </summary>
    public Expression? Initializer { get; internal set; }

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}