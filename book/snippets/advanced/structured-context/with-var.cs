using Standard;

namespace Snippet;

public class Greeter
{
    public static string GetGreeting()
    {
        var message = (string)MistContext.Peek<string>();
        return message;
    }

    public Greeter()
    {
    }

    public static Greeter __CreateDefault()
    {
        return new Greeter();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        {
            var greeting = "hello";
            MistContext.Push<string>(greeting);
            try
            {
                System.Console.WriteLine(greeting);
                string result = Snippet.Greeter.GetGreeting();
                System.Console.WriteLine(result);
            }
            finally
            {
                MistContext.Pop<string>();
            }
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
