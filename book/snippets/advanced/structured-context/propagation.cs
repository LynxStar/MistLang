using System;

namespace ContextDemo;

public class Consumer
{
    public static string GetMessage()
    {
        var msg = (string)MistContext.Peek<string>();
        return msg;
    }
}

public class Intermediary
{
    public static string Forward()
    {
        return Consumer.GetMessage();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var message = "propagated";
        MistContext.Push<string>(message);
        try
        {
            var result = Intermediary.Forward();
            Console.WriteLine(result);
        }
        finally
        {
            MistContext.Pop<string>();
        }
    }
}
