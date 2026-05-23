using Standard;
using System;

namespace Demo;

public class MathUtils
{
    public static int Add(int a, int b)
    {
        return a + b;
    }

    public static string Greet(string name)
    {
        return $"Hello, {name}!";
    }

    public static void Main(string[] args)
    {
        int result = Add(3, 4);
        System.Console.WriteLine($"Add(3, 4) = {result}");
        System.Console.WriteLine(Greet("Mist"));
    }

    public static MathUtils __CreateDefault()
    {
        var inst = new MathUtils();
        return inst;
    }
}
