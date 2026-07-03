using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A newtype definition in the program model.
/// </summary>
internal sealed class Newtype(
    string name, string namespaceName, Scope scope, MistType? declaringType)
    : MistType(name, namespaceName, scope, declaringType), INewtype;