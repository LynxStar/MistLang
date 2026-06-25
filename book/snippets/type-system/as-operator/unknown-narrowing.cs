using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        object box = 99;
        int? maybeInt = (box is int __as_tmp_0 ? __as_tmp_0 : default(int?));
        if (maybeInt is int n)
        {
            System.Console.WriteLine($"Got int: {n}");
        }
        else
        {
            System.Console.WriteLine("Not an int");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
