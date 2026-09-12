using Standard;

namespace Snippet;

public class Program
{
    public static Union_Int32_String Parse(string input)
    {
        if (input is "42")
        {
            return 42;
        }
        return input;
    }

    public static void Main(string[] args)
    {
        Union_Int32_String a = 100;
        Union_Int32_String b = "hello";
        Union_Int32_String c = Parse("42");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
