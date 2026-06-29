using System;

namespace Snippet;

public record class Season(byte Value)
{
    public static readonly Season Spring = new Season((byte)0);
    public static readonly Season Summer = new Season((byte)1);
    public static readonly Season Autumn = new Season((byte)2);
    public static readonly Season Winter = new Season((byte)3);

    public static implicit operator byte(Season e) => e.Value;

    public static explicit operator Season?(byte value)
    {
        if (Equals(value, Spring.Value)) return Spring;
        else if (Equals(value, Summer.Value)) return Summer;
        else if (Equals(value, Autumn.Value)) return Autumn;
        else if (Equals(value, Winter.Value)) return Winter;
        return null;
    }

    public override string ToString()
    {
        if (ReferenceEquals(this, Spring)) return "Spring";
        else if (ReferenceEquals(this, Summer)) return "Summer";
        else if (ReferenceEquals(this, Autumn)) return "Autumn";
        else if (ReferenceEquals(this, Winter)) return "Winter";
        return Value.ToString();
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var weather = Season.Winter;
        var region = Season.Summer;
        var advisory = (weather, region) switch
        {
            (var __e0, var __e1) when __e0 == Season.Winter && __e1 == Season.Winter => "stay indoors",
            (var __e0, var __e1) when __e0 == Season.Summer && __e1 == Season.Summer => "stay hydrated",
            (var __e0, var _) when __e0 == Season.Winter => "pack warm layers",
            (var _, var __e0) when __e0 == Season.Winter => "expect cold snap",
            var _ => "no advisory",
        };
        Console.WriteLine($"advisory: {advisory}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
