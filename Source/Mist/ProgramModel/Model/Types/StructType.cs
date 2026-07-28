using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A struct type definition in the program model.
/// </summary>
internal sealed class StructType(
    string name, string namespaceName, Scope? parentScope, MistType? declaringType, TypeDeclarationNode? declarationNode)
    : ConcreteType(name, namespaceName, parentScope, declaringType, declarationNode),
    IStructType
{
    /// <summary>
    /// Whether this struct is a ref struct (stack-only, no heap allocation).
    /// </summary>
    public bool IsRef => Modifiers.IsRef;
}