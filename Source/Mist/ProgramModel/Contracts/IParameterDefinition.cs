using Mist.ProgramModel.Contracts.TypeReferences;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A parameter of a method, constructor, indexer, or operator.
/// </summary>
public interface IParameterDefinition
{
    /// <summary>The parameter name.</summary>
    string Name { get; }

    /// <summary>The resolved type of the parameter.</summary>
    ITypeReference Type { get; }

    /// <summary>How the parameter is passed (by value, by ref, etc.).</summary>
    ParameterPassingKind PassingKind { get; }

    /// <summary>Whether the parameter has a default value.</summary>
    bool HasDefaultValue { get; }
}