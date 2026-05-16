using System;

namespace ScopingDemo;

public class DeepShadowing
{
    public static void Main(string[] args)
    {
        var name = "first";

        {
            var name = "second";
            {
                var name = "third";
                Console.WriteLine(name);
            }
            Console.WriteLine(name);
        }

        Console.WriteLine(name);
    }
}
