using Standard;

namespace Snippet;

public class Gauge
{
    public int Level;

    public Gauge(int level)
    {
        this.Level = default(int);
        this.Level = level;
    }

    public int Doubled()
    {
        return Level * 2;
    }

    public void Raise()
    {
        this.Level = Level + 1;
    }
}

public class Panel
{
    public Snippet.Gauge Main = new Snippet.Gauge(3);

    public Panel()
    {
    }

    public static Panel __CreateDefault()
    {
        return new Panel();
    }
}

public class Program
{
    public static int Report(Snippet.Panel panel)
    {
        return panel.Main.Level + panel.Main.Doubled();
    }

    public static void Main(string[] args)
    {
        Snippet.Panel panel = new Snippet.Panel();
        panel.Main.Raise();
        System.Console.WriteLine(System.FormattableString.Invariant($"report={Report(panel)}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
