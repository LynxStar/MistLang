using System;

namespace Hello;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Console.WriteLine("Hello, World!");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
