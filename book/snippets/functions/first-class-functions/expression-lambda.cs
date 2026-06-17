using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Func<int, int> square = x => x * x;
        System.Func<int, int, int> multiply = (x, y) => x * y;
        System.Func<string> hello = () => "Hello!";
        System.Console.WriteLine($"square(6)={square(6)}");
        System.Console.WriteLine($"multiply(3,7)={multiply(3, 7)}");
        System.Console.WriteLine($"hello()={hello()}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
