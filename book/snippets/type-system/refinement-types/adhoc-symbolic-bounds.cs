using Standard;

namespace Snippet;

public class Buffer<TSize>
    where TSize : IConstIntegral
{
    public static int Size => ((int)TSize.Value);

    public int At(int index)
    {
        return index;
    }

    public Buffer()
    {
    }

    public static Buffer<TSize> __CreateDefault()
    {
        return new Buffer<TSize>();
    }
}

public class Tally
{
    public int Count
    {
        get
        {
            return 3;
        }
    }

    public Tally()
    {
    }

    public static Tally __CreateDefault()
    {
        return new Tally();
    }
}

public class Ranges
{
    public static int Width(int lo, int hi)
    {
        return hi - lo;
    }

    public Ranges()
    {
    }

    public static Ranges __CreateDefault()
    {
        return new Ranges();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine(new Snippet.Buffer<Const_4>().At(3).ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(Snippet.TallyExtensions_adhocsymbolicbounds_0.Pick(new Snippet.Tally(), 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(Snippet.Ranges.Width(2, 5).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}

public static class TallyExtensions_adhocsymbolicbounds_0
{
    public static int Pick(this Snippet.Tally self, int n)
    {
        return n;
    }
}
