using Mist.Backends.CSharpTranspiler.AmbientProvider;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A tuple as the CLR lays it out, which only C#'s tuple syntax hides: a ValueTuple holds seven elements
/// and nests the rest under <c>Rest</c> as a ValueTuple of their own, so a nine-element tuple keeps its
/// last two in a pair and its ninth element lives at <c>Rest.Item2</c>. Anything naming the ValueTuple
/// outright -- a type test, a path into one -- has to follow that layout, there being no flat ValueTuple
/// past seven.
/// </summary>
internal static class ValueTupleNesting
{
    public static string Spelled(IReadOnlyList<string> elements)
    {
        if(elements.Count == 0)
        {
            return "System.ValueTuple";
        }

        if(elements.Count <= ClrValueTuple.RestPosition)
        {
            return $"System.ValueTuple<{string.Join(", ", elements)}>";
        }

        var rest = Spelled([.. elements.Skip(ClrValueTuple.RestPosition)]);

        return $"System.ValueTuple<{string.Join(", ", elements.Take(ClrValueTuple.RestPosition))}, {rest}>";
    }

    public static string Path(int position)
    {
        var depth = position / ClrValueTuple.RestPosition;

        return string.Concat(Enumerable.Repeat("Rest.", depth)) + $"Item{position % ClrValueTuple.RestPosition + 1}";
    }
}