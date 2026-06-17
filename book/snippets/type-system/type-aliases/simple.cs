using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        int id = 42;
        string name = "Alice";
        double score = 98.5;
        bool active = true;
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
