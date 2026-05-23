using Standard;
using System;

namespace UnknownDemo;

public class Program
{
    public static void Main(string[] args)
    {
        (int, object) source = (7, "seven");
        var (num, data) = source;
        System.Console.WriteLine($"num={num}");
        if (data is string ds)
        {
            System.Console.WriteLine($"data narrowed to string: {ds}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
