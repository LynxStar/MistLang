using Mist.Generated;
using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A class type definition in the program model.
/// </summary>
internal sealed class ClassDefinition(
    string name, string namespaceName, Scope scope, TypeDefinition? declaringType, TypeDeclarationNode declarationNode)
    : ConcreteTypeDefinition(name, namespaceName, scope, declaringType, declarationNode),
    IClassDefinition;
