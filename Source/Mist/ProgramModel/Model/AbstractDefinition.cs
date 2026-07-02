using Mist.Generated;
using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An abstract (interface-like) type definition in the program model.
/// </summary>
internal sealed class AbstractDefinition(
    string name, string namespaceName, Scope scope, TypeDefinition? declaringType, TypeDeclarationNode declarationNode)
    : MemberTypeDefinition(name, TypeDefinitionKind.Abstract, namespaceName, scope, declaringType, declarationNode),
    IAbstractDefinition;