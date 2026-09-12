using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? x = null;
        x ??= 100;
        int narrowed = x.Value;
        System.Console.WriteLine($"x={x.Value}");
        System.Console.WriteLine($"narrowed={narrowed}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
