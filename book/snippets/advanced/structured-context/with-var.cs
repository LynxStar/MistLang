using System;

namespace ContextDemo;

public class Greeter
{
    public static string GetGreeting()
    {
        var message = (string)MistContext.Peek<string>();
        return message;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var greeting = "hello";
        MistContext.Push<string>(greeting);
        try
        {
            Console.WriteLine(greeting);
            var result = Greeter.GetGreeting();
            Console.WriteLine(result);
        }
        finally
        {
            MistContext.Pop<string>();
        }
    }
}
