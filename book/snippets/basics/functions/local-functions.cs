using System;

namespace Snippet;

public class Program
{
    public static void Main()
    {
        Console.WriteLine(Describe(3));
        string Describe(int n)
        {
            if (IsEven(n))
            {
                return $"{n} is even";
            }
            return $"{n} is odd";
        }
        bool IsEven(int n)
        {
            return n % 2 == 0;
        }
        int calls = 0;
        void Count()
        {
            calls = calls + 1;
        }
        Count();
        Count();
        Console.WriteLine($"calls={calls}");
        static int Square(int x)
        {
            return x * x;
        }
        Console.WriteLine(Square(7));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
