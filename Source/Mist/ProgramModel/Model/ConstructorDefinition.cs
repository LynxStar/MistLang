using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A constructor declared on a type.
/// </summary>
internal sealed class ConstructorDefinition(
    IReadOnlyList<IParameterDefinition> parameters) : IConstructorDefinition
{
    public IReadOnlyList<IParameterDefinition> Parameters { get; } = parameters;
}