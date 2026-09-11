using System;

namespace Snippet;

public class Program
{
    public static string DescribeVoid(Standard.Void v)
    {
        return "received void";
    }

    public static void Main(string[] args)
    {
        string desc = DescribeVoid(Standard.Void.Instance);
        Console.WriteLine($"DescribeVoid: {desc}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
