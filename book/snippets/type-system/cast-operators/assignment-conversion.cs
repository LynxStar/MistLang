using Standard;
using System;

namespace CastOperators;

public class Program
{
    public static void Main(string[] args)
    {
        var boiling = new Celsius(100.0);
        Fahrenheit boilingF = boiling;
        System.Console.WriteLine(boilingF.Describe());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
