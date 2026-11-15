using Standard;

namespace Snippet;

public class Program
{
    public static void Reserve(int needed, int available)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Reserved {needed} of {available}"));
    }

    public static void Demo(int used, int capacity)
    {
        if (used <= capacity)
        {
            int doubled = capacity * 2;
            Reserve(used, doubled);
        }
    }

    public static void Main(string[] args)
    {
        Demo(3, 4);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
