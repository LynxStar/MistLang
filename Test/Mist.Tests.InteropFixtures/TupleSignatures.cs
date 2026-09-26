namespace Mist.Tests.InteropFixtures;

// Nine and more elements are the CLR's Rest encoding: ValueTuple`8 whose last argument is a further
// ValueTuple, so Octet ends in ValueTuple`1 and Sixteen nests two levels deep.
public static class TupleSignatures
{
    public static (int Min, int Max) Bounds = (1, 10);

    public static (string First, string Last) Author { get; } = ("Ada", "Lovelace");

    public static (int, string) MakePair(int number, string text)
    {
        return (number, text);
    }

    public static string DescribePair((int, string) pair)
    {
        return $"{pair.Item1}:{pair.Item2}";
    }

    public static (int Quotient, int Remainder) Divide(int dividend, int divisor)
    {
        return (dividend / divisor, dividend % divisor);
    }

    public static int Area((int Width, int Height) size)
    {
        return size.Width * size.Height;
    }

    public static (int, string Label) PartlyNamed(int number)
    {
        return (number, $"#{number}");
    }

    public static ((int X, int Y) Origin, string Label) Locate(int x, int y, string label)
    {
        return ((x, y), label);
    }

    public static string DescribeLocation(((int X, int Y) Origin, string Label) location)
    {
        return $"{location.Label}@{location.Origin.X},{location.Origin.Y}";
    }

    public static (int One, int Two, int Three, int Four, int Five, int Six, int Seven, int Eight) Octet(int start)
    {
        return (start, start + 1, start + 2, start + 3, start + 4, start + 5, start + 6, start + 7);
    }

    public static (int A, int B, int C, int D, int E, int F, int G, int H, string Tail) Nonet(string tail)
    {
        return (1, 2, 3, 4, 5, 6, 7, 8, tail);
    }

    public static string DescribeNonet((int, int, int, int, int, int, int, int, string) values)
    {
        var sum = values.Item1 + values.Item2 + values.Item3 + values.Item4
            + values.Item5 + values.Item6 + values.Item7 + values.Item8;
        return $"{values.Item9}={sum}";
    }

    public static (int, int, int, int, int, int, int, int, int, int, int, int, int, int, (int Inner, string Label) Pair, string Last) Sixteen(string last)
    {
        return (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, (15, "fifteen"), last);
    }

    public static string DescribeSixteen((int, int, int, int, int, int, int, int, int, int, int, int, int, int, (int, string), string) values)
    {
        return $"{values.Item1}..{values.Item14} {values.Item15.Item2} {values.Item16}";
    }

    public static IEnumerable<(string Name, int Score)> Scores()
    {
        return [("ada", 3), ("grace", 5)];
    }

    public static int Total(List<(string Name, int Score)> scores)
    {
        return scores.Sum(entry => entry.Score);
    }

    public static Dictionary<(int Row, int Column), (string Name, bool Occupied)> Grid()
    {
        return new() { [(0, 1)] = ("rook", true) };
    }

    public static (int, int)[] Pairs(int count)
    {
        return Enumerable.Range(0, count).Select(i => (i, i * i)).ToArray();
    }

    public static (int Low, int High)? Span(bool present)
    {
        return present ? (2, 9) : null;
    }

    public static void Swap(ref (int Left, int Right) pair)
    {
        pair = (pair.Right, pair.Left);
    }

    public static (Predicate<(int X, int Y)> Test, (int Low, int High) Window) Filter()
    {
        return (point => point.X < point.Y, (1, 5));
    }

    public static ValueTuple<int> Single(int value)
    {
        return new ValueTuple<int>(value);
    }
}