using System;

namespace Demo;

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
}

public class Program
{
    public static void Main(string[] args)
    {
        var cfg = new Config { Timeout = 60, Verbose = true, Name = "production" };
        Console.WriteLine($"Timeout={cfg.Timeout}, Name={cfg.Name}");
    }
}
