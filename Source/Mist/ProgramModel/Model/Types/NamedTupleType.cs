using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Element names over a structural <see cref="TupleType"/>. Names are cosmetic and do NOT affect
/// identity: this is not interned but created per declaration site, so any code that keys on type
/// identity (interning, equality) must see through it to <see cref="Underlying"/>.
/// <para>
/// Members mirror C# named tuples: <see cref="Fields"/> holds the positional <c>Item1..ItemN</c>
/// fields followed by one alias per named element, so both <c>pair.Item1</c> and <c>pair.x</c>
/// resolve to a field.
/// </para>
/// </summary>
internal sealed class NamedTupleType : MistType, INamedTupleType
{
    private readonly List<Field> fields;

    public NamedTupleType(TupleType underlying, IReadOnlyList<string?> elementNames)
        : base(BuildName(underlying.ElementTypes, elementNames), new Scope(null), declaringType: null)
    {
        Underlying = underlying;
        ElementNames = elementNames;
        fields = BuildFields(underlying, elementNames);
    }

    public TupleType Underlying { get; }

    public IReadOnlyList<string?> ElementNames { get; }

    public IReadOnlyList<IMistType> ElementTypes => Underlying.ElementTypes;

    ITupleType INamedTupleType.Underlying => Underlying;

    IReadOnlyList<string?> INamedTupleType.ElementNames => ElementNames;

    IReadOnlyList<IMistType> ITupleType.ElementTypes => Underlying.ElementTypes;

    public IReadOnlyList<Field> Fields => fields;

    private static List<Field> BuildFields(TupleType underlying, IReadOnlyList<string?> elementNames)
    {
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