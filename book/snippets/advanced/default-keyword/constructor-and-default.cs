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


    protected Config()
    {
        this.Environment = "development";
        this.MaxRetries = 1;
    }

    public static Config __CreateDefault()
    {
        var inst = new Config();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var prod = new Config("production");
        Config dev = Config.__CreateDefault();
        Console.WriteLine($"new: env={prod.Environment} retries={prod.MaxRetries}");
        Console.WriteLine($"default: env={dev.Environment} retries={dev.MaxRetries}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
