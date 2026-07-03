using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A constructor declared on a type.
/// </summary>
internal sealed class Constructor(
    IReadOnlyList<IParameter> parameters,
    MemberModifiers modifiers) : IConstructor
{
    public IReadOnlyList<IParameter> Parameters { get; } = parameters;

    public MemberModifiers Modifiers { get; } = modifiers;
}