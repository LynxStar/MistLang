using System;

namespace Snippet;

public class Config
{
    public int MaxRetries;
    public bool Verbose;
    public string Label;

    public Config()
    {
        this.MaxRetries = default(int);
        this.Verbose = default(bool);
        this.Label = "";
    }

    public static Config __CreateDefault()
    {
        return new Config();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Config inferred = Snippet.Config.__CreateDefault();
        Console.WriteLine($"MaxRetries={inferred.MaxRetries} Verbose={inferred.Verbose} Label={inferred.Label}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
