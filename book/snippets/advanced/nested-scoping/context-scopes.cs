using Standard;

namespace Snippet;

public class Logger
{
    public static void Log(string msg)
    {
        var prefix = (string)MistContext.Peek<string>();
        System.Console.WriteLine($"[{prefix}] {msg}");
    }

    public Logger()
    {
    }

    public static Logger __CreateDefault()
    {
        return new Logger();
    }
}

public class ContextScopeDemo
{
    public static void Main(string[] args)
    {
        {
            var prefix = "OUTER";
            MistContext.Push<string>(prefix);
            try
            {
                Snippet.Logger.Log("message 1");
                {
                    {
                        var prefix_1 = "INNER";
                        MistContext.Push<string>(prefix_1);
                        try
                        {
                            Snippet.Logger.Log("message 2");
                        }
                        finally
                        {
                            MistContext.Pop<string>();
                        }
                    }
                }
                Snippet.Logger.Log("message 3");
            }
            finally
            {
                MistContext.Pop<string>();
            }
        }
    }

    public ContextScopeDemo()
    {
    }

    public static ContextScopeDemo __CreateDefault()
    {
        return new ContextScopeDemo();
    }
}
