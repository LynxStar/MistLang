using Standard;

namespace Snippet;

public class Counter
{
    private static int totalCreated;
    private string name;

    public Counter(string name)
    {
        this.name = name;
        totalCreated = totalCreated + 1;
    }

    public static int GetTotalCreated()
    {
        return totalCreated;
    }

    public static void Main(string[] args)
    {
        Snippet.Counter a = new Snippet.Counter("Alpha");
        Snippet.Counter b = new Snippet.Counter("Beta");
        System.Console.WriteLine(System.FormattableString.Invariant($"Total created: {Snippet.Counter.GetTotalCreated()}"));
    }
}
