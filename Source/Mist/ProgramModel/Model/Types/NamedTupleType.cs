using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A declaration-site tuple type that decorates a structural <see cref="TupleType"/>
/// with element names (e.g., <c>(int x, bool flag)</c>). Names are cosmetic and do
/// NOT affect identity — <see cref="Underlying"/> is the identity-bearing type.
/// <para>
/// Members mirror C# named tuples: the positional <c>Item1..ItemN</c> fields from the
/// underlying type are exposed via <see cref="Fields"/> alongside one aliasing field per
/// named element, so both <c>pair.Item1</c> and <c>pair.x</c> resolve to a field. Unnamed
/// elements contribute no alias.
/// </para>
/// <para>
/// Not interned: created once per declaration site where the names are written.
/// Any code that keys on type identity (interning, equality) must see through this
/// decorator to <see cref="Underlying"/>.
/// </para>
/// </summary>
internal sealed class NamedTupleType : MistType, INamedTupleType
{
    private readonly List<Field> fields;

    public NamedTupleType(TupleType underlying, IReadOnlyList<string?> elementNames)
        : base(BuildName(underlying.ElementTypes, elementNames), "", new Scope(null), declaringType: null)
    {
        Underlying = underlying;
        ElementNames = elementNames;
        fields = BuildFields(underlying, elementNames);
    }

    /// <summary>
    /// The underlying structural tuple type (interned, name-free).
    /// </summary>
    public TupleType Underlying { get; }

    /// <summary>
    /// Element names in positional order. Null entries indicate unnamed elements.
    /// </summary>
    public IReadOnlyList<string?> ElementNames { get; }

    public IReadOnlyList<IMistType> ElementTypes => Underlying.ElementTypes;

    ITupleType INamedTupleType.Underlying => Underlying;

    IReadOnlyList<string?> INamedTupleType.ElementNames => ElementNames;

    IReadOnlyList<IMistType> ITupleType.ElementTypes => Underlying.ElementTypes;

    /// <summary>
    /// The tuple's fields: the positional <c>Item1..ItemN</c> fields followed by
    /// one alias field per named element.
    /// </summary>
    public IReadOnlyList<Field> Fields => fields;

    private static List<Field> BuildFields(TupleType underlying, IReadOnlyList<string?> elementNames)
    {
        // Positional Item1..ItemN come first, then one alias per named element.
        var result = new List<Field>(underlying.PositionalFields);
        var elementTypes = underlying.ElementTypes;

        for(var i = 0; i < elementTypes.Count; i++)
        {
            if(i < elementNames.Count && elementNames[i] is { } name)
            {
                result.Add(new Field(
                    name,
                    elementTypes[i],
                    OwnershipKind.Unrestricted,
                    new MemberModifiers { Accessibility = Accessibility.Public }));
            }
        }

        return result;
    }

    private static string BuildName(IReadOnlyList<IMistType> elementTypes, IReadOnlyList<string?> elementNames)
    {
        var parts = new string[elementTypes.Count];

        for(var i = 0; i < elementTypes.Count; i++)
        {
            parts[i] = i < elementNames.Count && elementNames[i] is { } name
                ? $"{elementTypes[i].Name} {name}"
                : elementTypes[i].Name;
        }

        return $"({string.Join(", ", parts)})";
    }
}