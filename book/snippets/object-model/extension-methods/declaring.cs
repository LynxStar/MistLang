using System;

namespace ExtensionMethods;

public class Greeting
{
    public string Message;

    public Greeting(string message)
    {
        Message = message;
    }
}

public static class GreetingExtensions_GreetingExtensions_L14
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
