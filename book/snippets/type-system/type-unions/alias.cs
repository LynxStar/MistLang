using System;

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
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        Union_Int32_String a = 100;
        Union_Int32_String b = "hello";
        var c = Parse("42");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
