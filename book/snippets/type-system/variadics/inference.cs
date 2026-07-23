using System;

namespace Snippet;

public class Program
{
    public static int Describe<T1, T2, T3>(System.ValueTuple<T1, T2, T3> args)
    {
        return 3;
    }

    public static void Main(string[] cmdArgs)
    {
        // Both calls emit identically -- inference resolves to the same arity
        var a = Describe((1, "x", true));

        var b = Describe<int, string, bool>((1, "x", true));

        System.Console.WriteLine($"{a} {b}");
    }
}
