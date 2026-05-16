using System;

namespace ContextDemo;

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
        var cfg = (Config)MistContext.Peek<Config>();
        return cfg.Value;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        MistContext.Push<Config>(new Config("discarded-binding"));
        try
        {
            var result = Consumers.GetConfig();
            Console.WriteLine(result);
        }
        finally
        {
            MistContext.Pop<Config>();
        }
    }
}
