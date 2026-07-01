using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A struct type definition in the program model.
/// </summary>
internal sealed class StructDefinition(
    string name, string namespaceName, Scope scope, TypeDefinition? declaringType)
    : TypeDefinition(name, TypeDefinitionKind.Struct, namespaceName, scope, declaringType), IStructDefinition;