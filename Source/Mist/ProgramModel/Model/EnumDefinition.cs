using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An enum type definition in the program model.
/// </summary>
internal sealed class EnumDefinition(
    string name, string namespaceName, Scope scope, TypeDefinition? declaringType)
    : TypeDefinition(name, namespaceName, scope, declaringType), IEnumDefinition;