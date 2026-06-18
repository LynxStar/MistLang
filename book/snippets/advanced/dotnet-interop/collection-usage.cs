using System;
using System.Collections.Generic;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        System.Console.WriteLine($"Count={list.Count}");
        System.Console.WriteLine($"First={list[0]}");
        foreach (int item in list)
        {
            System.Console.WriteLine($"Item={item}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
