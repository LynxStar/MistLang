using System;

namespace Snippet;

public class Validator
{
    public static string Process(int x)
    {
        return $"Process({x}) accepted";
    }

    public static string Range(int min, int max)
    {
        return $"Range({min}, {max}) accepted";
    }

    public static Validator __CreateDefault()
    {
        var inst = new Validator();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var result = Validator.Process(42);
        Console.WriteLine(result);
        var range = Validator.Range(5, 500);
        Console.WriteLine(range);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
