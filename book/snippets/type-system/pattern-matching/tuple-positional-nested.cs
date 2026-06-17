using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var nested = ((1, 2), "label");
        if (nested is ((> 0, > 0), "label"))
        {
            Console.WriteLine("nested tuple: both positive with label");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
