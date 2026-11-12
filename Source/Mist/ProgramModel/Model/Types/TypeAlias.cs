using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Includes refinement types.
/// </summary>
internal sealed class TypeAlias(
    string name, string namespaceName, Scope? parentScope, MistType? declaringType, TypeAliasDeclarationNode declarationNode)
    : DeclaredType(name, namespaceName, parentScope, declaringType), ITypeAlias, IHasTypeParameters
{
    internal TypeAliasDeclarationNode DeclarationNode { get; } = declarationNode;

    public List<TypeParameter> TypeParametersList { get; } = [];

    IReadOnlyList<ITypeParameter> IGenericDeclaration.TypeParameters => TypeParametersList;

    public MemberModifiers Modifiers { get; internal set; }

    public IMistType? AliasedType { get; internal set; }

    internal bool IsFailed { get; set; }

    IMistType? ITypeAlias.AliasedType => AliasedType;
}