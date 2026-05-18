using System;

namespace DefaultDemo;

public class Config
{
    public int MaxRetries;

    public bool Verbose;

    public string Label;

    protected Config()
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
        Config cfg = Config.__CreateDefault();
        Console.WriteLine($"MaxRetries={cfg.MaxRetries} Verbose={cfg.Verbose} Label={cfg.Label}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
