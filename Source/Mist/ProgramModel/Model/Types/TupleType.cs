using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A tuple type with ordered element types and optional element names.
/// Interned by element types only — names are cosmetic and do not affect identity.
/// </summary>
internal sealed class TupleType : MistType, ITupleType
{
    public TupleType(IReadOnlyList<IMistType> elementTypes, IReadOnlyList<string?> elementNames)
        : base(BuildName(elementTypes, elementNames), "", new Scope(null), declaringType: null)
    {
        ElementTypes = elementTypes;
        ElementNames = elementNames;
    }

    /// <summary>
    /// The resolved element types in positional order.
    /// </summary>
    public IReadOnlyList<IMistType> ElementTypes { get; }

    /// <summary>
    /// Optional element names in positional order.
    /// </summary>
    public IReadOnlyList<string?> ElementNames { get; }

    IReadOnlyList<IMistType> ITupleType.ElementTypes => ElementTypes;

    IReadOnlyList<string?> ITupleType.ElementNames => ElementNames;

    private static string BuildName(IReadOnlyList<IMistType> elementTypes, IReadOnlyList<string?> elementNames)
    {
        var parts = new string[elementTypes.Count];

        for(var i = 0; i < elementTypes.Count; i++)
        {
            parts[i] = elementNames[i] is { } name
                ? $"{elementTypes[i].Name} {name}"
                : elementTypes[i].Name;
        }

        return $"({string.Join(", ", parts)})";
    }
}