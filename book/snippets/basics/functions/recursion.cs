using Standard;

namespace Snippet;

public class Program
{
    public static int Factorial(int n)
    {
        if (n <= 1)
        {
            return 1;
        }
        return n * Factorial(n - 1);
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"5! = {Factorial(5)}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"10! = {Factorial(10)}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
