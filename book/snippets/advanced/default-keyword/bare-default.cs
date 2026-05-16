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
        Config inferred = Config.__CreateDefault();
        Console.WriteLine($"MaxRetries={inferred.MaxRetries} Verbose={inferred.Verbose} Label={inferred.Label}");
    }
}
