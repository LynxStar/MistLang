using Standard;

namespace Snippet;

public sealed record class Season(byte Value)
{
    public static readonly Season Spring = new Season(0);
    public static readonly Season Summer = new Season(1);
    public static readonly Season Autumn = new Season(2);
    public static readonly Season Winter = new Season(3);

    public static implicit operator byte(Season e) => e.Value;

    public static explicit operator Season?(byte value)
    {
        if(Equals(value, Season.Spring.Value)) return Season.Spring;
        if(Equals(value, Season.Summer.Value)) return Season.Summer;
        if(Equals(value, Season.Autumn.Value)) return Season.Autumn;
        if(Equals(value, Season.Winter.Value)) return Season.Winter;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, Season.Spring)) return "Spring";
        if(ReferenceEquals(this, Season.Summer)) return "Summer";
        if(ReferenceEquals(this, Season.Autumn)) return "Autumn";
        if(ReferenceEquals(this, Season.Winter)) return "Winter";
        return Value.ToString();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Season weather = Snippet.Season.Winter;
        Snippet.Season region = Snippet.Season.Summer;
        string advisory = (weather, region) switch { (var __e1, var __e2) when __e1 == Snippet.Season.Winter && __e2 == Snippet.Season.Winter => "stay indoors", (var __e3, var __e4) when __e3 == Snippet.Season.Summer && __e4 == Snippet.Season.Summer => "stay hydrated", (var __e5, var _) when __e5 == Snippet.Season.Winter => "pack warm layers", (var _, var __e6) when __e6 == Snippet.Season.Winter => "expect cold snap", var _ => "no advisory" };
        System.Console.WriteLine($"advisory: {advisory}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
