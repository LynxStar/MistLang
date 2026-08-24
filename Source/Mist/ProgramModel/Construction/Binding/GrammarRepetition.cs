using System.Collections.Immutable;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The operands of a repeating grammar rule, in source order. Two shapes, and reading one as the other
/// silently loses or duplicates an operand -- which is what makes this a shared home rather than a walk
/// per caller.
/// <para>
/// The generated parser declares a variable for the repetition element, reassigns it per iteration AND
/// pushes it into the array. So the LAST named slot of a rule that repeats is a duplicate of the array's
/// final entry, never an operand of its own. What separates the two shapes is where the required
/// operands sit relative to that: <c>a or b or c</c> is one required operand and a repetition, so
/// <see cref="LeadAndRepeated"/> reads the lead and the array; a tuple is TWO required operands and a
/// repetition, so <see cref="TwoLeadsAndRepeated"/> reads both leads and the array. Either way the
/// duplicating slot is not read.
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