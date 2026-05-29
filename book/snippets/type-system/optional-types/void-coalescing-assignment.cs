using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int x = MistVoid.Instance;
        if (x.IsVoid)
        {
            x = 100;
        }
        int narrowed = x;
        Console.WriteLine($"x={x}");
        Console.WriteLine($"narrowed={narrowed}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
