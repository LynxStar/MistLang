using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// <c>[p, .. q, r]</c>. Positional elements match against what the scrutinee holds at that position and
/// a slice matches the contiguous middle, so a positional element's scrutinee is the element type and a
/// slice's is the slice type -- two different types within one pattern, which is what separates this
/// from <see cref="TuplePattern"/>.
/// <para>
/// Keeps no binding of its own for <see cref="PropertyPattern"/>'s reason: the grammar spells no
/// trailing name here at all, so every binder a list pattern contributes lives on an element, the slice
/// included.
/// </para>
/// </summary>
internal sealed class ListPattern(
    SyntaxNode node,
    IReadOnlyList<ListPatternElement> elements,
    IMistType narrowedType) : Pattern(node)
{
    public IReadOnlyList<ListPatternElement> Elements { get; } = elements;

    public override IMistType NarrowedType { get; } = narrowedType;

    public CountedIndexer? Reads { get; init; }

    public override LocalVariable? Binding => null;
}

/// <summary>
/// One element of a list pattern. <see cref="Subpattern"/> is null only on a slice, <c>..</c> being the
/// one element form C# spells with no pattern after it.
/// </summary>
internal sealed record ListPatternElement(Pattern? Subpattern, bool IsSlice);