using System;
using System.Collections.Generic;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, bool> isPositive = x => x > 0;
        System.Delegate baseDelegate = isPositive;
        System.Console.WriteLine($"delegateType={baseDelegate.GetType().Name}");
        System.Predicate<int> pred = new System.Predicate<int>(x => x < 10);
        System.Delegate fromNamed = pred;
        System.Console.WriteLine($"fromNamedType={fromNamed.GetType().Name}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
