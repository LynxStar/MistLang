using System;

namespace Snippet;

public class Program
{
    private static void Describe(Union_Int32_String value)
    {
        if (value.IsInt32)
        {
            int n = value.AsInt32;
            Console.WriteLine($"  int: {n}");
        }
        if (value.IsString)
        {
            string s = value.AsString;
            Console.WriteLine($"  string: {s}");
        }
    }

    private static Union_Int32_String GetValue(bool useInt)
    {
        if (useInt)
        {
            return 100;
        }
        return "hello";
    }

    public static void Main(string[] args)
    {
        Program.Describe(7);
        Program.Describe("Mist");
        Union_Int32_String result = Program.GetValue(true);
        Console.WriteLine($"GetValue(true) returned a value");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
