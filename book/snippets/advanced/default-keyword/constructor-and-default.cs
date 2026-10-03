using Standard;

namespace Snippet;

public class Config
{
    public string Environment;
    public int MaxRetries;

    public Config(string env)
    {
        this.Environment = env;
        this.MaxRetries = 3;
    }

    protected Config(Mist.Emission.CSharp.__DefaultInit marker)
    {
        this.Environment = "development";
        this.MaxRetries = 1;
    }

    public static Config __CreateDefault()
    {
        return new Config(default(Mist.Emission.CSharp.__DefaultInit));
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Config prod = new Snippet.Config("production");
        Snippet.Config dev = Snippet.Config.__CreateDefault();
        System.Console.WriteLine(System.FormattableString.Invariant($"new: env={prod.Environment} retries={prod.MaxRetries}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"default: env={dev.Environment} retries={dev.MaxRetries}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
