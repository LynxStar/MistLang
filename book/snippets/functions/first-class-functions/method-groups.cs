using Standard;

namespace Snippet;

public class Program
{
    public static int Negate(int x)
    {
        return -x;
    }

    public static void Main(string[] args)
    {
        System.Func<int, int> fn = Negate;
        System.Console.WriteLine($"fn(42)={fn(42)}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
