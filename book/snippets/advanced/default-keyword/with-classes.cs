using System;

namespace Snippet;

public class Config
{
    public int MaxRetries;

    public bool Verbose;

    public string Label;

    public Config()
    {
        this.MaxRetries = 0;
        this.Verbose = false;
        this.Label = "";
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
        Config cfg = Config.__CreateDefault();
        Console.WriteLine($"MaxRetries={cfg.MaxRetries} Verbose={cfg.Verbose} Label={cfg.Label}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
