using Standard;

namespace Snippet;

public class Container
{
    public int Value;

    public Container(int value)
    {
        this.Value = value;
    }

    public string Describe()
    {
        return $"Container({Value})";
    }
}

public class Program
{
    private static void ReplaceContainer(ref Snippet.Container c)
    {
        c = new Snippet.Container(999);
    }

    public static void Main()
    {
        Snippet.Container box = new Snippet.Container(42);
        System.Console.WriteLine($"before: {box.Describe()}");
        ReplaceContainer(ref box);
        System.Console.WriteLine($"after: {box.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
