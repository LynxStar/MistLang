using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Action<string> printer = (string msg) => Console.WriteLine($"[LOG] {msg}");
        System.Action doNothing = () => 
        {
        }
;
        printer("hello");
        doNothing();
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
