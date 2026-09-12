using Standard;
using System.Collections.Generic;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Collections.Generic.List<int> list = new System.Collections.Generic.List<int>();
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

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
