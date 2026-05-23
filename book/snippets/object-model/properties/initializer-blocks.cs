using System;

namespace Snippet;

public class Config
{
    public int Timeout { get; set; } = 0;

    public bool Verbose { get; set; } = false;

    public string Name { get; set; } = "";

    public Config()
    {
        this.Timeout = 0;
        this.Verbose = false;
        this.Name = "";
    }

    public static Config __CreateDefault()
    {
        var inst = new Config();
        inst.Timeout = 0;
        inst.Verbose = false;
        inst.Name = "";
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var cfg = new Config() { Timeout = 60, Verbose = true, Name = "production" };
        Console.WriteLine($"Timeout={cfg.Timeout}, Name={cfg.Name}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
