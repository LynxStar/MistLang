using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A constructor declared on a type.
/// </summary>
internal sealed class Constructor(
    IReadOnlyList<IParameter> parameters) : IConstructor
{
    public IReadOnlyList<IParameter> Parameters { get; } = parameters;
}