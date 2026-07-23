using System;

public class Program
{
    public static int Describe<T1, T2, T3>(System.ValueTuple<T1, T2, T3> args)
    {
        return 3;
    }

    public static void Main(string[] cmdArgs)
    {
        var arity = Describe((1, "x", true));
        System.Console.WriteLine($"arity={arity}");
    }
}
