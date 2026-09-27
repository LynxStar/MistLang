using Standard;

namespace Snippet;

public class Validator
{
    public static string Process(int x)
    {
        return System.FormattableString.Invariant($"Process({x}) accepted");
    }

    public static string Range(int min, int max)
    {
        return System.FormattableString.Invariant($"Range({min}, {max}) accepted");
    }

    public Validator()
    {
    }

    public static Validator __CreateDefault()
    {
        return new Validator();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        string result = Snippet.Validator.Process(42);
        System.Console.WriteLine(result);
        string range = Snippet.Validator.Range(5, 500);
        System.Console.WriteLine(range);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
