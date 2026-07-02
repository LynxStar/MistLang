using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A newtype definition in the program model.
/// </summary>
internal sealed class NewtypeDefinition(
    string name, string namespaceName, Scope scope, TypeDefinition? declaringType)
    : TypeDefinition(name, namespaceName, scope, declaringType), INewtypeDefinition;
