using Standard;

namespace Snippet;

public class Reading
{
    public int Value;
    public string Unit;

    public Reading(int value, string unit)
    {
        this.Value = default(int);
        this.Unit = "";
        this.Value = value;
        this.Unit = unit;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"{Value} {Unit}");
    }
}

public class Recorded
{
    public readonly int Value;
    public readonly string Unit;

    public Recorded(int value, string unit)
    {
        this.Value = default(int);
        this.Unit = "";
        this.Value = value;
        this.Unit = unit;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"{Value} {Unit}");
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Reading live = new Snippet.Reading(20, "C");
        live.Value = 21;
        Snippet.Recorded recorded = new Snippet.Recorded(19, "C");
        System.Console.WriteLine($"live={live.Describe()} recorded={recorded.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
