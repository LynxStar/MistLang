using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A type alias definition (includes refinement types) in the program model.
/// </summary>
internal sealed class TypeAlias(
    string name, string namespaceName, Scope? parentScope, MistType? declaringType, TypeAliasDeclarationNode declarationNode)
    : DeclaredType(name, namespaceName, parentScope, declaringType), ITypeAlias, IHasTypeParameters
{
    /// <summary>
    /// The AST declaration node, stashed during Phase 1 for Phase 2 resolution.
    /// </summary>
    internal TypeAliasDeclarationNode DeclarationNode { get; } = declarationNode;

    /// <summary>
    /// The type parameters declared on this alias. Empty for non-generic aliases.
    /// </summary>
    public List<TypeParameter> TypeParametersList { get; } = [];

    IReadOnlyList<ITypeParameter> IGenericDeclaration.TypeParameters => TypeParametersList;

    /// <summary>
    /// The modifiers declared on this type alias.
    /// </summary>
    public MemberModifiers Modifiers { get; internal set; }

    /// <summary>
    /// The resolved type this alias refers to.
    /// </summary>
    public IMistType? AliasedType { get; internal set; }

    IMistType? ITypeAlias.AliasedType => AliasedType;
}