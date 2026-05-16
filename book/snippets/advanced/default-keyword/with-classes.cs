using System;

namespace DefaultDemo;

public class Config
{
    public int MaxRetries;
    public bool Verbose;
    public string Label;

    protected Config() { }

    public static Config __CreateDefault()
    {
        var inst = new Config();
        inst.MaxRetries = default(int);
        inst.Verbose = default(bool);
        inst.Label = "";
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
}
