using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? hasValue = 10;
        int? noValue = null;
        int result1 = hasValue ?? 0;
        int result2 = noValue ?? 42;
        System.Console.WriteLine(System.FormattableString.Invariant($"hasValue ?? 0={result1}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"noValue ?? 42={result2}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
