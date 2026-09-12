using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String x = 42;
        Union_Int32_String y = "hello";
        System.Console.WriteLine($"x holds an int");
        System.Console.WriteLine($"y holds a string");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
