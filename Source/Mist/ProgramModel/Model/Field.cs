using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A field declared on a type.
/// </summary>
internal sealed class Field(
    string name,
    ITypeReference type,
    OwnershipKind ownership,
    bool isReadonly,
    bool isStatic,
    FieldDeclarationNode declarationNode) : IField
{
    public string Name { get; } = name;

    public ITypeReference Type { get; } = type;

    public OwnershipKind Ownership { get; } = ownership;

    public bool IsReadonly { get; } = isReadonly;

    public bool IsStatic { get; } = isStatic;

    /// <summary>
    /// The AST node for this field. Used by later phases to process
    /// the initializer expression.
    /// </summary>
    internal FieldDeclarationNode DeclarationNode { get; } = declarationNode;
}