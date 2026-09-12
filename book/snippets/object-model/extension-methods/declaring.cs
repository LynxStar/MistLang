using Standard;

namespace Snippet;

public class Greeting
{
    public string Message;

    public Greeting(string message)
    {
        Message = message;
    }
}

public static class GreetingExtensions_declaring_0
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
