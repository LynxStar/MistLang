using System;

namespace Snippet;

public class Program
{
    public static int Add2(int a, int b)
    {
        return a + b;
    }

    public static void Main(string[] cmdArgs)
    {
        var pair = (10, 20);
        var result = Add2(pair.Item1, pair.Item2);
        System.Console.WriteLine($"result={result}");
    }
}
