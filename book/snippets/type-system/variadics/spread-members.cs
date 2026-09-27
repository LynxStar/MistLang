using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] cmdArgs)
    {
        Bundle_3<int, string, bool> bundle = new Bundle_3<int, string, bool>((42, "hi", true));
        System.Console.WriteLine(System.FormattableString.Invariant($"agg={bundle.Item.Item1}:{bundle.Item.Item2}:{bundle.Item.Item3}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"item1={bundle.Item1}"));
        System.Func<int, string, bool, string> fmt = (int a, string b, bool c) => System.FormattableString.Invariant($"{a}-{b}-{c}");
        string mapped = bundle.Map<string>(fmt);
        System.Console.WriteLine($"map={mapped}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
