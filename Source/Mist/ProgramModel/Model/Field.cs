using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A field declared on a type.
/// </summary>
internal sealed class Field(
    string name,
    IMistType type,
    OwnershipKind ownership,
    MemberModifiers modifiers,
    FieldDeclarationNode declarationNode) : IField
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    public OwnershipKind Ownership { get; } = ownership;

    public MemberModifiers Modifiers { get; } = modifiers;

    /// <summary>
    /// The field initializer expression, or null if none.
    /// </summary>
    public Expression? Initializer { get; init; }

    /// <summary>
    /// The AST node for this field. Used by later phases for additional processing.
    /// </summary>
    internal FieldDeclarationNode DeclarationNode { get; } = declarationNode;
}