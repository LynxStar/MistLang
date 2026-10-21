using System.Collections.Immutable;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The operands of a repeating grammar rule, in source order. Two shapes, and reading one as the other
/// silently loses or duplicates an operand -- which is what makes this a shared home rather than a walk
/// per caller.
/// <para>
/// The repeated operands are only in the array; a label on the repeated element names no property of its
/// own. What separates the two shapes is how many required operands precede the array: <c>a or b or c</c>
/// is one required operand and a repetition, so <see cref="LeadAndRepeated"/> reads the lead and the
/// array; a tuple is TWO required operands and a repetition, so <see cref="TwoLeadsAndRepeated"/> reads
/// both leads and the array.
/// </para>
/// </summary>
internal static class GrammarRepetition
{
    public static List<T> LeadAndRepeated<T>(T lead, ImmutableArray<T> repeated)
    {
        var operands = new List<T>(1 + repeated.Length) { lead };
        operands.AddRange(repeated);

        return operands;
    }

    public static List<T> TwoLeadsAndRepeated<T>(T first, T second, ImmutableArray<T> repeated)
    {
        var operands = new List<T>(2 + repeated.Length) { first, second };
        operands.AddRange(repeated);

        return operands;
    }
}