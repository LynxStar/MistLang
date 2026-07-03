using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A type alias definition (includes refinement types) in the program model.
/// </summary>
internal sealed class TypeAlias(
    string name, string namespaceName, Scope scope, MistType? declaringType)
    : MistType(name, namespaceName, scope, declaringType), ITypeAlias;