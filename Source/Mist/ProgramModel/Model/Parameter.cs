using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A parameter of a method, constructor, indexer, or operator.
/// </summary>
internal sealed class Parameter(
    string name,
    IMistType type,
    ParameterPassingKind passingKind,
    bool hasDefaultValue) : IParameter
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    public ParameterPassingKind PassingKind { get; } = passingKind;

    public bool HasDefaultValue { get; } = hasDefaultValue;
}