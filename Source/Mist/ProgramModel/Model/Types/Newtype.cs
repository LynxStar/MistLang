using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A newtype definition in the program model.
/// </summary>
internal sealed class Newtype(
    string name, string namespaceName, Scope? parentScope, MistType? declaringType, NewtypeDeclarationNode declarationNode)
    : DeclaredType(name, namespaceName, parentScope, declaringType), INewtype
{
    /// <summary>
    /// The AST declaration node, stashed during Phase 1 for Phase 2 resolution.
    /// </summary>
    internal NewtypeDeclarationNode DeclarationNode { get; } = declarationNode;

    /// <summary>
    /// The type parameters declared on this newtype. Empty for non-generic newtypes.
    /// </summary>
    public List<TypeParameter> TypeParametersList { get; } = [];

    /// <summary>
    /// The modifiers declared on this newtype.
    /// </summary>
    public MemberModifiers Modifiers { get; internal set; }

    /// <summary>
    /// The resolved source type this newtype wraps.
    /// </summary>
    public IMistType? SourceType { get; internal set; }

    IMistType? INewtype.SourceType => SourceType;
}