using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

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

    /// <summary>
    /// True when this parameter uses the Flattened_Call_Convention — a value-side
    /// <c>params</c> parameter (a Value_Pack <c>params TArgs args</c> of tuple type
    /// <c>(TArgs...)</c>, or a homogeneous Value_Level_Params <c>params T[]</c>) whose
    /// caller supplies a flat, comma-separated argument list rather than a single
    /// packaged argument. False for ordinary parameters, including a plainly typed
    /// tuple parameter <c>(TArgs...) args</c> declared without <c>params</c>.
    /// </summary>
    bool HasFlattenedCallConvention { get; }
}