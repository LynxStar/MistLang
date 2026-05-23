using Standard;
using System;

namespace Hello;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine("Hello, World!");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
