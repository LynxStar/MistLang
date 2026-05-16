using System;

namespace ExtensionMethods;

public class Program
{
    public static void Main(string[] args)
    {
        var greeting = new Greeting("Hello beautiful world");

        // Compiler rewrites dot-notation to static method calls
        var wc = GreetingExtensions_GreetingExtensions_L14.WordCount(greeting);
        var upper = GreetingExtensions_GreetingExtensions_L14.Uppercase(greeting);
        var repeated = GreetingExtensions_GreetingExtensions_L5.Repeat(greeting, 2);

        Console.WriteLine(wc);
        Console.WriteLine(upper);
    }
}
