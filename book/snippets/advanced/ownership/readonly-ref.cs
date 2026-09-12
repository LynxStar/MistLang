using Standard;

namespace Snippet;

public class ReadonlyRefDemo
{
    public static void ReadPair(ref readonly Snippet.Pair p)
    {
        int sum = p.X + p.Y;
        System.Console.WriteLine($"readonly ref sum={sum}");
    }

    public static void Run()
    {
        Snippet.Pair p = new Snippet.Pair(3, 7);
        ReadPair(in p);
        p.SetX(100);
        System.Console.WriteLine($"after readonly ref={p.Describe()}");
    }

    public ReadonlyRefDemo()
    {
    }

    public static ReadonlyRefDemo __CreateDefault()
    {
        return new ReadonlyRefDemo();
    }
}
