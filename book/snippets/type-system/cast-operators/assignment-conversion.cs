using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Celsius boiling = new Snippet.Celsius(100.0);
        Snippet.Fahrenheit boilingF = boiling;
        System.Console.WriteLine(boilingF.Describe());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
