using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Interned by element types: this is the identity-bearing tuple. Element names live on
/// <see cref="NamedTupleType"/>.
/// </summary>
internal sealed class TupleType : MistType, ITupleType
{
    public TupleType(IReadOnlyList<IMistType> elementTypes)
        : base(BuildName(elementTypes), new Scope(null), declaringType: null)
    {
        ElementTypes = elementTypes;
        PositionalFields = BuildPositionalFields(elementTypes);
    }

    public IReadOnlyList<IMistType> ElementTypes { get; }

    public IReadOnlyList<Field> PositionalFields { get; }

    IReadOnlyList<IMistType> ITupleType.ElementTypes => ElementTypes;

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