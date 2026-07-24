using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A type alias definition (includes refinement types) in the program model.
/// </summary>
internal sealed class TypeAlias(
    string name, string namespaceName, Scope scope, MistType? declaringType, TypeAliasDeclarationNode declarationNode)
    : MistType(name, namespaceName, scope, declaringType), ITypeAlias
{
    /// <summary>
    /// The AST declaration node, stashed during Phase 1 for Phase 2 resolution.
    /// </summary>
    internal TypeAliasDeclarationNode DeclarationNode { get; } = declarationNode;

    /// <summary>
    /// The type parameters declared on this alias. Empty for non-generic aliases.
    /// </summary>
    public List<TypeParameter> TypeParametersList { get; } = [];

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