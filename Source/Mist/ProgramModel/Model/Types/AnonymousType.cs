using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// The type of a <c>new { X = 1, Y = 2 }</c> literal. Interned on its ordered named members, so two
/// independently written literals of the same shape ARE one type: that makes them assignable to each
/// other, and it is how C# identifies its own anonymous types, so the identity survives emission.
/// <para>
/// ORDER IS PART OF THE SHAPE, because it is part of C#'s. Two literals differing only in member order
/// are two types in the emitted program, so they must be two here; a set would unify them and emission
/// would then silently assign between unrelated C# types.
/// </para>
/// </summary>
internal sealed class AnonymousType : MistType
{
    public AnonymousType(IReadOnlyList<string> memberNames, IReadOnlyList<IMistType> memberTypes)
        : base(BuildName(memberNames, memberTypes), new Scope(null), declaringType: null)
    {
        MemberNames = memberNames;
        Fields = BuildFields(memberNames, memberTypes);
    }

    public IReadOnlyList<string> MemberNames { get; }

    public IReadOnlyList<Field> Fields { get; }

    private static List<Field> BuildFields(
        IReadOnlyList<string> memberNames, IReadOnlyList<IMistType> memberTypes)
    {
        var fields = new List<Field>(memberNames.Count);

        for(var i = 0; i < memberNames.Count; i++)
        {
            fields.Add(new Field(
                memberNames[i],
                memberTypes[i],
                OwnershipKind.Unrestricted,
                new MemberModifiers { Accessibility = Accessibility.Public }));
        }

        return fields;
    }

    private static string BuildName(
        IReadOnlyList<string> memberNames, IReadOnlyList<IMistType> memberTypes)
    {
        var parts = new string[memberNames.Count];

        for(var i = 0; i < memberNames.Count; i++)
        {
            parts[i] = $"{memberNames[i]}: {memberTypes[i].Name}";
        }

        return $"{{ {string.Join(", ", parts)} }}";
    }
}