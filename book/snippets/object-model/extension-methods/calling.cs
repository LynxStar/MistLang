using System;

namespace Snippet;

public class Greeting
{
    public string Message;

    public Greeting(string message)
    {
        Message = message;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Greeting greeting = new Snippet.Greeting("Hello beautiful world");
        int wc = greeting.WordCount();
        string upper = greeting.Uppercase();
        Snippet.Greeting repeated = greeting.Repeat(2);
        Console.WriteLine(wc);
        Console.WriteLine(upper);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}

public static class GreetingExtensions_calling_0
{
    public static Snippet.Greeting Repeat(this Snippet.Greeting self, int times)
    {
        string repeated = "";
        for (int i = 0; i < times; i = i + 1)
        {
            if (i > 0)
            {
                repeated = repeated + " ";
            }
            repeated = repeated + self.Message;
        }
        return new Snippet.Greeting(repeated);
    }
}

public static class GreetingExtensions_calling_1
{
    public static int WordCount(this Snippet.Greeting self)
    {
        return self.Message.Length;
    }

    public static string Uppercase(this Snippet.Greeting self)
    {
        return self.Message;
    }
}
