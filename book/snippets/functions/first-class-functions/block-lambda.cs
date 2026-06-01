using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, string> classify = (int n) => { if (n > 0) { return "positive"; } if (n < 0) { return "negative"; } return "zero"; };
        System.Console.WriteLine($"classify(5)={classify(5)}");
        System.Console.WriteLine($"classify(-1)={classify(-1)}");
        System.Console.WriteLine($"classify(0)={classify(0)}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
