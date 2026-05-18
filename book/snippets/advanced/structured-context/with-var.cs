using System;

namespace ContextDemo;

public class Greeter
{
    public static string GetGreeting()
    {
        var message = (string)MistContext.Peek<string>();
        return message;
    }

    public static Greeter __CreateDefault()
    {
        var inst = new Greeter();
        return inst;
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

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
