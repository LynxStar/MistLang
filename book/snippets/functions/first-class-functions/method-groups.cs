using System;

namespace Snippet;

public class Program
{
    public static int Negate(int x)
    {
        return -x;
    }

    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Func<int, int> fn = Negate;
        System.Console.WriteLine($"fn(42)={fn(42)}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
