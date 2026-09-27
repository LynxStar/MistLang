using Standard;

namespace Snippet;

public class Pair
{
    public int X;
    public int Y;

    public Pair()
    {
        this.X = default(int);
        this.Y = default(int);
    }

    public static Pair __CreateDefault()
    {
        return new Pair();
    }
}

public class Factory
{
    public static T Create<T>(System.Func<T> __default_T)
    {
        return __default_T();
    }

    public Factory()
    {
    }

    public static Factory __CreateDefault()
    {
        return new Factory();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Pair p = Snippet.Factory.Create<Snippet.Pair>(() => Snippet.Pair.__CreateDefault());
        System.Console.WriteLine(System.FormattableString.Invariant($"X={p.X} Y={p.Y}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
