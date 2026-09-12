using Standard;

namespace Snippet;

public class Program
{
    public static int Apply(int value, System.Func<int, int> transform)
    {
        return transform(value);
    }

    public static System.Func<int, int> MakeMultiplier(int factor)
    {
        return (int x) => x * factor;
    }

    public static void Main(string[] args)
    {
        int result = Apply(10, (int x) => x + 5);
        System.Console.WriteLine($"Apply(10, x=>x+5)={result}");
        System.Func<int, int> triple = MakeMultiplier(3);
        System.Console.WriteLine($"triple(7)={triple(7)}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
