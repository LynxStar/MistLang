using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A parameter of a method, constructor, indexer, or operator.
/// </summary>
internal sealed class Parameter(
    string name,
    IMistType type,
    ParameterModifier modifier = default,
    bool hasFlattenedCallConvention = false) : IParameter, IHasAttributes
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    public ParameterModifier Modifier { get; } = modifier;

    /// <summary>
    /// True when this parameter uses the Flattened_Call_Convention — a value-side
    /// <c>params</c> parameter whose caller supplies a flat, comma-separated argument
    /// list at the call site rather than a single packaged argument. This covers a
    /// Value_Pack (<c>params TArgs args</c>, whose type is the tuple <c>(TArgs...)</c>)
    /// and a homogeneous Value_Level_Params (<c>params T[] xs</c>). It is <c>false</c>
    /// for an ordinary parameter, including a plainly typed tuple parameter
    /// <c>(TArgs...) args</c> declared without <c>params</c>, which receives a single
    /// tuple argument. The <c>params</c> modifier is the sole marker of this convention.
    /// </summary>
    public bool HasFlattenedCallConvention { get; } = hasFlattenedCallConvention;

    /// <summary>
    /// The attributes applied to this parameter.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    /// <summary>
    /// The adhoc refinement predicate AST node for this parameter, if any
    /// (from a <c>where paramName >= 0</c> clause). Null until resolved.
    /// Interpretation deferred to the pattern resolution phase.
    /// </summary>
    public Storm.Runtime.SyntaxNode? RefinementNode { get; internal set; }

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}