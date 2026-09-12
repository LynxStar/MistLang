using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String value = 42;
        Union_Boolean_Int32_String widened = value;
        {
            if (widened.AsInt32OrNull is int n)
            {
                System.Console.WriteLine($"widened int: {n}");
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
