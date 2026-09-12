using Standard;
using System.Collections.Generic;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, bool> isPositive = (int x) => x > 0;
        System.Delegate baseDelegate = isPositive;
        System.Console.WriteLine($"delegateType={baseDelegate.GetType().Name}");
        System.Predicate<int> pred = new System.Predicate<int>((int x) => x < 10);
        System.Delegate fromNamed = pred;
        System.Console.WriteLine($"fromNamedType={fromNamed.GetType().Name}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
