using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A method declared on a type.
/// </summary>
internal sealed class Method(
    string name,
    IMistType returnType,
    IReadOnlyList<IParameter> parameters,
    MemberModifiers modifiers) : IMethod
{
    public string Name { get; } = name;

    public IMistType ReturnType { get; } = returnType;

    public IReadOnlyList<IParameter> Parameters { get; } = parameters;

    public MemberModifiers Modifiers { get; } = modifiers;
}