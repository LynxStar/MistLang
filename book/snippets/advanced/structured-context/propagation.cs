using Standard;

namespace Snippet;

public class Consumer
{
    public static string GetMessage()
    {
        var msg = (string)MistContext.Peek<string>();
        return msg;
    }

    public Consumer()
    {
    }

    public static Consumer __CreateDefault()
    {
        return new Consumer();
    }
}

public class Intermediary
{
    public static string Forward()
    {
        return Snippet.Consumer.GetMessage();
    }

    public Intermediary()
    {
    }

    public static Intermediary __CreateDefault()
    {
        return new Intermediary();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        {
            var message = "propagated";
            MistContext.Push<string>(message);
            try
            {
                string result = Snippet.Intermediary.Forward();
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
