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
        Config inferred = default;
        Console.WriteLine($"MaxRetries={inferred.MaxRetries} Verbose={inferred.Verbose} Label={inferred.Label}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
