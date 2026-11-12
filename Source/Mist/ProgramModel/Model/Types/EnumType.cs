using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

internal sealed class EnumType(
    string name,
    string namespaceName,
    Scope? parentScope,
    MistType? declaringType,
    EnumDeclarationNode? declarationNode)
    : DeclaredType(name, namespaceName, parentScope, declaringType), IEnumType, IHasAttributes
{
    internal EnumDeclarationNode? DeclarationNode { get; } = declarationNode;

    public MemberModifiers Modifiers { get; internal set; }

    public IMistType? UnderlyingType { get; internal set; }

    public List<EnumMember> MembersList { get; } = [];

    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    IMistType? IEnumType.UnderlyingType => UnderlyingType;

    IReadOnlyList<IEnumMember> IEnumType.Members => MembersList;

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}