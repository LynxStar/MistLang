using System;
using System.Threading;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int value = 42;
        int old = System.Threading.Interlocked.Exchange(ref value, 100);
        Console.WriteLine($"old={old}, new={value}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
