using System;

namespace Snippet;

public class Program
{
    private static void DescribeTriple(Union_Boolean_Int32_String value)
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
        if (value.IsBoolean)
        {
            bool b = value.AsBoolean;
            Console.WriteLine($"  bool: {b}");
        }
    }

    public static void Main(string[] args)
    {
        Union_Boolean_Int32_String tri1 = 99;
        Union_Boolean_Int32_String tri2 = "triple";
        Union_Boolean_Int32_String tri3 = true;
        Program.DescribeTriple(tri1);
        Program.DescribeTriple(tri2);
        Program.DescribeTriple(tri3);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
