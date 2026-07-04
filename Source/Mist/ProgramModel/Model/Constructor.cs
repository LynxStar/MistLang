using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A constructor declared on a type. Constructors are methods whose return
/// type is the declaring type and whose name is the type name.
/// </summary>
internal sealed class Constructor(
    string name,
    IMistType returnType,
    IReadOnlyList<IParameter> parameters,
    MemberModifiers modifiers)
    : Method(name, returnType, parameters, modifiers), IConstructor
{
}
