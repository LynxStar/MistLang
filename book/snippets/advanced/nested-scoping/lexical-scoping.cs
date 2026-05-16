using System;

namespace ScopingDemo;

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
}
