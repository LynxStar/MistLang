using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// An enum type definition in the program model.
/// </summary>
internal sealed class EnumType(
    string name,
    string namespaceName,
    Scope scope,
    MistType? declaringType,
    EnumDeclarationNode? declarationNode)
    : MistType(name, namespaceName, scope, declaringType), IEnumType, IHasAttributes
{
    /// <summary>
    /// The AST declaration node, stashed during Phase 1 for Phase 2 resolution.
    /// Null for assembly-projected types.
    /// </summary>
    internal EnumDeclarationNode? DeclarationNode { get; } = declarationNode;

    /// <summary>
    /// The modifiers declared on this enum type.
    /// </summary>
    public MemberModifiers Modifiers { get; internal set; }

    /// <summary>
    /// The resolved underlying type (from explicit annotation, or null if not yet resolved).
    /// </summary>
    public IMistType? UnderlyingType { get; internal set; }

    /// <summary>
    /// The enum's declared members (variants).
    /// </summary>
    public List<EnumMember> MembersList { get; } = [];

    /// <summary>
    /// The attributes applied to this enum type.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    IMistType? IEnumType.UnderlyingType => UnderlyingType;

    IReadOnlyList<IEnumMember> IEnumType.Members => MembersList;

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}