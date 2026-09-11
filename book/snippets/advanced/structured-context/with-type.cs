using System;

namespace Snippet;

public class Config
{
    public string Value;

    public Config(string value)
    {
        this.Value = value;
    }
}

public class Consumers
{
    public static string GetConfig()
    {
        var cfg = (Snippet.Config)MistContext.Peek<Snippet.Config>();
        return cfg.Value;
    }

    public Consumers()
    {
    }

    public static Consumers __CreateDefault()
    {
        return new Consumers();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        {
            var cfg = new Snippet.Config("production");
            MistContext.Push<Snippet.Config>(cfg);
            try
            {
                Console.WriteLine(cfg.Value);
                string result = Snippet.Consumers.GetConfig();
                Console.WriteLine(result);
            }
            finally
            {
                MistContext.Pop<Snippet.Config>();
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
