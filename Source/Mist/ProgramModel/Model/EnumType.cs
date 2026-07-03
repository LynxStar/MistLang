using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An enum type definition in the program model.
/// </summary>
internal sealed class EnumType(
    string name, string namespaceName, Scope scope, MistType? declaringType)
    : MistType(name, namespaceName, scope, declaringType), IEnumType;