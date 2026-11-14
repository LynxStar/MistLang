using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Members;

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

    public Field? StorageSource { get; init; }

    public int? StorageElementIndex { get; init; }

    public MemberType? DeclaringType { get; init; }

    public Storm.Runtime.SyntaxNode? DeclarationNode { get; init; }

    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    /// <summary>
    /// Settable for the same reason <see cref="Statements.Block.Statements"/> is: signature
    /// resolution runs before any body binding, so Phase 2 parks the unbound expression here and
    /// Phase 3 replaces it with the resolved one. Nothing reads it in between.
    /// </summary>
    public Expression? Initializer { get; internal set; }

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}