using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, int> square = (int x) => x * x;
        System.Func<int, int, int> multiply = (int x, int y) => x * y;
        System.Func<string> hello = () => "Hello!";
        Console.WriteLine($"square(6)={square(6)}");
        Console.WriteLine($"multiply(3,7)={multiply(3, 7)}");
        Console.WriteLine($"hello()={hello()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
