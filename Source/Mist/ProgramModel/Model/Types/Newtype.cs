using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A newtype definition in the program model.
/// </summary>
internal sealed class Newtype(
    string name, string namespaceName, Scope scope, MistType? declaringType, NewtypeDeclarationNode declarationNode)
    : MistType(name, namespaceName, scope, declaringType), INewtype
{
    /// <summary>
    /// The AST declaration node, stashed during Phase 1 for Phase 2 resolution.
    /// </summary>
    internal NewtypeDeclarationNode DeclarationNode { get; } = declarationNode;

    /// <summary>
    /// The resolved source type this newtype wraps.
    /// </summary>
    public IMistType? SourceType { get; internal set; }

    IMistType? INewtype.SourceType => SourceType;
}