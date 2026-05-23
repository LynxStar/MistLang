using System;

namespace Snippet;

public class Consumer
{
    public static string GetMessage()
    {
        var msg = (string)MistContext.Peek<string>();
        return msg;
    }

    public static Consumer __CreateDefault()
    {
        var inst = new Consumer();
        return inst;
    }
}
public class Intermediary
{
    public static string Forward()
    {
        return Consumer.GetMessage();
    }

    public static Intermediary __CreateDefault()
    {
        var inst = new Intermediary();
        return inst;
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

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
