using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 42;
        int? maybeInt = value.AsInt32OrNull;
        {
            if (maybeInt is int n)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"Got int: {n}"));
            }
            else
            {
                System.Console.WriteLine("Not an int");
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
