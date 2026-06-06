using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var outer = "visible everywhere";
        {
            var inner = "only visible in this block";
            Console.WriteLine(inner);
            Console.WriteLine(outer);
        }
        Console.WriteLine(outer);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
