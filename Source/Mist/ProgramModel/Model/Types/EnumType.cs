using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// An enum type definition in the program model.
/// </summary>
internal sealed class EnumType(
    string name, string namespaceName, Scope scope, MistType? declaringType)
    : MistType(name, namespaceName, scope, declaringType), IEnumType;