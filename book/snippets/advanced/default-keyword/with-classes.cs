using Standard;

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
        Snippet.Config cfg = Snippet.Config.__CreateDefault();
        System.Console.WriteLine($"MaxRetries={cfg.MaxRetries} Verbose={cfg.Verbose} Label={cfg.Label}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
