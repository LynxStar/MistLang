using Standard;

namespace Snippet;

public class Counter
{
    private static int totalCreated;
    private string name;
    private int value;

    public Counter(string name)
    {
        this.name = "";
        this.value = default(int);
        this.name = name;
        this.value = 0;
        totalCreated = totalCreated + 1;
    }

    public void Increment()
    {
        this.value = this.value + 1;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Counter '{name}' = {value}");
    }

    public static void Main(string[] args)
    {
        Snippet.Counter c = new Snippet.Counter("Alpha");
        c.Increment();
        c.Increment();
        System.Console.WriteLine(c.Describe());
    }
}
