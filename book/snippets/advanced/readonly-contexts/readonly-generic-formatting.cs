using Standard;
using System;

namespace Snippet;

public class Celsius : System.IFormattable
{
    public double Degrees;

    public Celsius(double degrees)
    {
        this.Degrees = default(double);
        this.Degrees = degrees;
    }

    public string ToString(string format, System.IFormatProvider provider)
    {
        return System.FormattableString.Invariant($"{Degrees} C");
    }
}

public class Program
{
    public static string Label<T>(T value)
    {
        return $"[{MistInvariant.Text(value)}]";
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine(Label<Snippet.Celsius>(new Snippet.Celsius(21.5)));
        System.Console.WriteLine(Label<int>(7));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
