using System;

public class Program
{
    public static TResult Apply<T1, T2, T3, TResult>(System.Func<T1, T2, T3, TResult> f, System.ValueTuple<T1, T2, T3> args)
    {
        return f(args.Item1, args.Item2, args.Item3);
    }

    public static void Main(string[] cmdArgs)
    {
        Func<int, int, int, int> add = (a, b, c) => a + b + c;
        var result = Apply(add, (2, 3, 4));
        System.Console.WriteLine($"forward={result}");
    }
}
