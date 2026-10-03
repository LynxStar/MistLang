using Standard;

namespace Snippet;

public class Config
{
    public Config()
    {
        this.Timeout = 0;
        this.Verbose = false;
        this.Name = "";
    }

    public int Timeout { get; set; } = 0;

    public bool Verbose { get; set; } = false;

    public string Name { get; set; } = "";
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Config cfg = new Snippet.Config() { Timeout = 60, Verbose = true, Name = "production" };
        System.Console.WriteLine(System.FormattableString.Invariant($"Timeout={cfg.Timeout}, Name={cfg.Name}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
