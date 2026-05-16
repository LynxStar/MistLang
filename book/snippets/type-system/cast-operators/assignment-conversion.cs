using System;

namespace CastOperators;

public class Program
{
    public static void Main(string[] args)
    {
        var boiling = new Celsius(100.0);
        Fahrenheit boilingF = ((Fahrenheit)boiling);
        Console.WriteLine(boilingF.Describe());
    }
}
