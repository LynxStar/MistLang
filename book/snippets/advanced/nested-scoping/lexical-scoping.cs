using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        string outer = "visible everywhere";
        {
            string inner = "only visible in this block";
            Console.WriteLine(inner);
            Console.WriteLine(outer);
        }
        Console.WriteLine(outer);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
