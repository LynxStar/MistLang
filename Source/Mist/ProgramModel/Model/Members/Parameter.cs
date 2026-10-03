using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// A parameter of a method, constructor, indexer, or operator.
/// </summary>
internal sealed class Parameter(
    string name,
    IMistType type,
    ParameterModifier modifier = default) : IParameter, IHasAttributes
{
    public string Name { get; } = name;

    public IMistType Type { get; internal set; } = type;

    public ParameterModifier Modifier { get; } = modifier;

    internal bool IsFailed { get; set; }

    /// <summary>
    /// Declared with <c>params</c>, so the call site supplies a flat comma-separated list where this
    /// one slot stands. True for both spellings: a value pack (<c>params TArgs args</c>), whose type
    /// is the tuple the pack expands to, and the homogeneous form (<c>params string[] parts</c>),
    /// whose type is a real array.
    /// </summary>
    public bool IsFlattened { get; internal set; }

    /// <summary>
    /// The attributes applied to this parameter.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}