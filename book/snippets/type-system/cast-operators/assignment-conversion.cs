using System;

namespace Snippet;

public class Kelvin
{
    private double degrees;

    public Kelvin(double degrees)
    {
        this.degrees = degrees;
    }

    public string Describe()
    {
        return $"{degrees} K";
    }
}

public class Celsius
{
    private double degrees;

    public Celsius(double degrees)
    {
        this.degrees = degrees;
    }

    public double GetDegrees()
    {
        return degrees;
    }

    public static implicit operator Snippet.Fahrenheit(Snippet.Celsius c)
    {
        return new Snippet.Fahrenheit(c.GetDegrees() * 1.8 + 32.0);
    }

    public static explicit operator Snippet.Kelvin(Snippet.Celsius c)
    {
        return new Snippet.Kelvin(c.GetDegrees() + 273.15);
    }
}

public class Fahrenheit
{
    private double degrees;

    public Fahrenheit(double degrees)
    {
        this.degrees = degrees;
    }

    public string Describe()
    {
        return $"{degrees} F";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Celsius boiling = new Snippet.Celsius(100.0);
        Snippet.Fahrenheit boilingF = boiling;
        Console.WriteLine(boilingF.Describe());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
