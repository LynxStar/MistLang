using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
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
