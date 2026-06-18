using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Action<string> printer = msg => System.Console.WriteLine($"[LOG] {msg}");
        System.Action doNothing = () => { };
        printer("hello");
        doNothing();
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
