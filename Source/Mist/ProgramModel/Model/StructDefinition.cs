using Mist.Generated;
using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A struct type definition in the program model.
/// </summary>
internal sealed class StructDefinition(
    string name, string namespaceName, Scope scope, TypeDefinition? declaringType, TypeDeclarationNode declarationNode)
    : ConcreteTypeDefinition(name, namespaceName, scope, declaringType, declarationNode),
    IStructDefinition;