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

public static class GreetingExtensions_withparams_0
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

public static class GreetingExtensions_withparams_1
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
