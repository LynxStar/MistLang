using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A parameter of a method, constructor, indexer, or operator.
/// </summary>
internal sealed class ParameterDefinition(
    string name,
    ITypeReference type,
    ParameterPassingKind passingKind,
    bool hasDefaultValue) : IParameterDefinition
{
    public string Name { get; } = name;

    public ITypeReference Type { get; } = type;

    public ParameterPassingKind PassingKind { get; } = passingKind;

    public bool HasDefaultValue { get; } = hasDefaultValue;
}