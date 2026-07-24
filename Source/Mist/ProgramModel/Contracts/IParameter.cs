using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A parameter of a method, constructor, indexer, or operator.
/// </summary>
public interface IParameter
{
    /// <summary>The parameter name.</summary>
    string Name { get; }

    /// <summary>The resolved type of the parameter.</summary>
    IMistType Type { get; }

    /// <summary>The readonly / ref / ownership modifiers applied to the parameter.</summary>
    ParameterModifier Modifier { get; }
}