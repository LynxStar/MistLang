using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Represents a single applied attribute with its resolved type and arguments.
/// </summary>
public sealed class AttributeData(
    IMistType attributeType,
    IReadOnlyList<object?> positionalArguments,
    IReadOnlyList<(string Name, object? Value)> namedArguments)
{
    /// <summary>The resolved attribute class type.</summary>
    public IMistType AttributeType { get; } = attributeType;

    /// <summary>Positional constructor arguments.</summary>
    public IReadOnlyList<object?> PositionalArguments { get; } = positionalArguments;

    /// <summary>Named property and field assignments.</summary>
    public IReadOnlyList<(string Name, object? Value)> NamedArguments { get; } = namedArguments;
}