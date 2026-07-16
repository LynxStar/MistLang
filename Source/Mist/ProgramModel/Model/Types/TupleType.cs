using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A structural tuple type with ordered element types. Interned by element types —
/// this is the identity-bearing tuple representation. Element names, when present at
/// a declaration site, live on <see cref="NamedTupleType"/>, not here.
/// <para>
/// Every tuple exposes its elements positionally as <c>Item1..ItemN</c> fields via
/// <see cref="PositionalFields"/>. Named aliases are layered on top by
/// <see cref="NamedTupleType"/>.
/// </para>
/// </summary>
internal sealed class TupleType : MistType, ITupleType
{
    public TupleType(IReadOnlyList<IMistType> elementTypes)
        : base(BuildName(elementTypes), "", new Scope(null), declaringType: null)
    {
        ElementTypes = elementTypes;
        PositionalFields = BuildPositionalFields(elementTypes);
    }

    /// <summary>
    /// The resolved element types in positional order.
    /// </summary>
    public IReadOnlyList<IMistType> ElementTypes { get; }

    /// <summary>
    /// The positional <c>Item1..ItemN</c> fields, one per element, in order.
    /// </summary>
    public IReadOnlyList<Field> PositionalFields { get; }

    IReadOnlyList<IMistType> ITupleType.ElementTypes => ElementTypes;

    /// <summary>
    /// The reserved positional field name for the 1-based element index
    /// <paramref name="oneBasedIndex"/> (e.g., 1 → <c>Item1</c>).
    /// </summary>
    public static string PositionalName(int oneBasedIndex)
    {
        return $"Item{oneBasedIndex}";
    }

    private static List<Field> BuildPositionalFields(IReadOnlyList<IMistType> elementTypes)
    {
        var fields = new List<Field>(elementTypes.Count);

        for(var i = 0; i < elementTypes.Count; i++)
        {
            fields.Add(new Field(
                PositionalName(i + 1),
                elementTypes[i],
                OwnershipKind.Unrestricted,
                new MemberModifiers { Accessibility = Accessibility.Public }));
        }

        return fields;
    }

    private static string BuildName(IReadOnlyList<IMistType> elementTypes)
    {
        var parts = new string[elementTypes.Count];

        for(var i = 0; i < elementTypes.Count; i++)
        {
            parts[i] = elementTypes[i].Name;
        }

        return $"({string.Join(", ", parts)})";
    }
}