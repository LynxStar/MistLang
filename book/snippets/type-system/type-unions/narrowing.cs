using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 42;
        {
            if (value.AsInt32OrNull is int n)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"It's an int: {n}"));
            }
        }
        {
            if (value.AsStringOrNull is string s)
            {
                System.Console.WriteLine($"It's a string: {s}");
            }
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
