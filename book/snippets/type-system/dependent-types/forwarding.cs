using Standard;

namespace Snippet;

public class WideCounter<TLimit>
    where TLimit : IConstIntegral
{
    public static long Limit => ((long)TLimit.Value);

    public long Remaining(long used)
    {
        return ((long)TLimit.Value) - used;
    }

    public WideCounter()
    {
    }

    public static WideCounter<TLimit> __CreateDefault()
    {
        return new WideCounter<TLimit>();
    }
}

public class NarrowCounter<TCap> : Snippet.WideCounter<TCap>
    where TCap : IConstIntegral
{
    public static int Cap => ((int)TCap.Value);

    public NarrowCounter()
    {
    }

    public new static NarrowCounter<TCap> __CreateDefault()
    {
        return new NarrowCounter<TCap>();
    }
}

public class Program
{
    public static void Main()
    {
        Snippet.WideCounter<Const_7> counter = new Snippet.NarrowCounter<Const_7>();
        System.Console.WriteLine(counter.Remaining(2).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
