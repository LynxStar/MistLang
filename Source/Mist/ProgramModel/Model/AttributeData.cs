using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

public sealed class AttributeData(
    IMistType attributeType,
    IReadOnlyList<object?> positionalArguments,
    IReadOnlyList<(string Name, object? Value)> namedArguments)
{
    public IMistType AttributeType { get; } = attributeType;

    public IReadOnlyList<object?> PositionalArguments { get; } = positionalArguments;

    public IReadOnlyList<(string Name, object? Value)> NamedArguments { get; } = namedArguments;
}