using System;

namespace Snippet;

public class Greeting
{
    public string Message;

    public Greeting(string message)
    {
        Message = message;
    }

    protected Greeting()
    {
        this.Message = "";
    }

    public static Greeting __CreateDefault()
    {
        var inst = new Greeting();
        return inst;
    }
}
public static class GreetingExtensions_declaring_L11
{
    public static int WordCount(this Greeting self)
    {
        return self.Message.Length;
    }

    public static string Uppercase(this Greeting self)
    {
        return self.Message;
    }
}
