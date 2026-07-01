using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A type alias definition (includes refinement types) in the program model.
/// </summary>
internal sealed class TypeAliasDefinition(
    string name, string namespaceName, Scope scope, TypeDefinition? declaringType)
    : TypeDefinition(name, TypeDefinitionKind.TypeAlias, namespaceName, scope, declaringType), ITypeAliasDefinition;