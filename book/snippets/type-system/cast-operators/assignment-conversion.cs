using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var boiling = new Celsius(100.0);
        Fahrenheit boilingF = boiling;
        Console.WriteLine(boilingF.Describe());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
