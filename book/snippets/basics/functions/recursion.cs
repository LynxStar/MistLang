using Standard;
using System;

namespace Demo;

public class Program
{
    public static int Factorial(int n)
    {
        if (n <= 1)
        {
            return 1;
        }
        return n * Factorial(n - 1);
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine($"5! = {Factorial(5)}");
        System.Console.WriteLine($"10! = {Factorial(10)}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
