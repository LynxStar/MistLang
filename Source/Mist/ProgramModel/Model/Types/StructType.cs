using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

internal sealed class StructType(
    string name, string namespaceName, Scope? parentScope, MistType? declaringType, TypeDeclarationNode? declarationNode)
    : ConcreteType(name, namespaceName, parentScope, declaringType, declarationNode),
    IStructType
{
    /// <summary>
    /// A ref struct: stack-only.
    /// </summary>
    public bool IsRef => Modifiers.IsRef;
}