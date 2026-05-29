using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int hasValue = 10;
        int noValue = MistVoid.Instance;
        int result1 = (hasValue.IsVoid ? 0 : hasValue.AsInt32);
        int result2 = (noValue.IsVoid ? 42 : noValue.AsInt32);
        Console.WriteLine($"hasValue ?? 0={result1}");
        Console.WriteLine($"noValue ?? 42={result2}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
