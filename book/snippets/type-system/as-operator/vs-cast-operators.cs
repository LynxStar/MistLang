using System;
using System.Text.RegularExpressions;

namespace Snippet;

public struct Celsius
{
    public double Degrees;

    public Celsius(double degrees)
    {
        Degrees = degrees;
    }

    public static explicit operator Fahrenheit(Celsius c)
    {
        return new Fahrenheit(c.Degrees * 9.0 / 5.0 + 32.0);
    }
}
public struct Fahrenheit
{
    public double Degrees;

    public Fahrenheit(double degrees)
    {
        Degrees = degrees;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var boiling = new Celsius(100.0);
        var f = ((Fahrenheit)boiling);
        System.Console.WriteLine($"{f.Degrees} F");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
