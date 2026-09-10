using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// The type of a <c>new { X = 1, Y = 2 }</c> literal: an ordered list of named members and nothing
/// else. Interned on that shape, so two independently written literals of the same shape ARE one
/// type -- which is what makes them assignable to each other, and is also exactly how C# identifies
/// its own anonymous types, so the structural identity survives emission rather than being
/// reconstructed there.
/// <para>
/// ORDER IS PART OF THE SHAPE, because it is part of C#'s. Two literals differing only in the order
/// they name the same members are two types in the emitted program, so they must be two here; a set
/// would unify them and emission would then silently assign between unrelated C# types.
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