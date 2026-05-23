using Standard;
using System;

namespace UnknownDemo;

public class Program
{
    public static void Main(string[] args)
    {
        object u = 42;
        System.Console.WriteLine(u);
        u = "hello";
        System.Console.WriteLine(u);
        u = true;
        System.Console.WriteLine(u);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
