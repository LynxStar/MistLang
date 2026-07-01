using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An abstract (interface-like) type definition in the program model.
/// </summary>
internal sealed class AbstractDefinition(
    string name, string namespaceName, Scope scope, TypeDefinition? declaringType)
    : TypeDefinition(name, TypeDefinitionKind.Abstract, namespaceName, scope, declaringType), IAbstractDefinition;