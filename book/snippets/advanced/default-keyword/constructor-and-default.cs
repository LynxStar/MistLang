using System;

namespace Snippet;

public class Config
{
    public string Environment;
    public int MaxRetries;

    public Config(string env)
    {
        this.Environment = env;
        this.MaxRetries = 3;
    }

    private Config(Mist.Emission.CSharp.__DefaultInit marker)
    {
        this.Environment = "development";
        this.MaxRetries = 1;
    }

    public static Config __CreateDefault()
    {
        return new Config(default(Mist.Emission.CSharp.__DefaultInit));
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Config prod = new Snippet.Config("production");
        Snippet.Config dev = Snippet.Config.__CreateDefault();
        Console.WriteLine($"new: env={prod.Environment} retries={prod.MaxRetries}");
        Console.WriteLine($"default: env={dev.Environment} retries={dev.MaxRetries}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
