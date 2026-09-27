using Standard;

namespace Snippet;

public class Counter
{
    private static int totalCreated;

    public Counter()
    {
        totalCreated = totalCreated + 1;
    }

    public static int GetTotalCreated()
    {
        return totalCreated;
    }

    public static void Main(string[] args)
    {
        Snippet.Counter c1 = new Snippet.Counter();
        Snippet.Counter c2 = new Snippet.Counter();
        System.Console.WriteLine(System.FormattableString.Invariant($"Total: {Snippet.Counter.GetTotalCreated()}"));
    }

    private Counter(Mist.Emission.CSharp.__DefaultInit marker)
    {
    }

    public static Counter __CreateDefault()
    {
        return new Counter(default(Mist.Emission.CSharp.__DefaultInit));
    }
}
