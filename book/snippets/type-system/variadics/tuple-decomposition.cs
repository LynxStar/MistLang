using Standard;

namespace Snippet;

public class Program
{
    public static int Add2(int a, int b)
    {
        return a + b;
    }

    public static void Main(string[] cmdArgs)
    {
        (int, int) pair = (10, 20);
        int result = Add2(pair.Item1, pair.Item2);
        System.Console.WriteLine(System.FormattableString.Invariant($"result={result}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
