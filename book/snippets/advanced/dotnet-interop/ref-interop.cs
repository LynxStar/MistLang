using System;
using System.Threading;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        int value = 42;
        int old = Interlocked.Exchange(ref value, 100);
        System.Console.WriteLine($"old={old}, new={value}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
