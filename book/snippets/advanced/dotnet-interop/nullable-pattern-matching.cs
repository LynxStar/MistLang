using System;
using System.Collections.Generic;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var dict = new Dictionary<string, int>();
        dict.Add("hello", 42);
        if (dict.ContainsKey("hello"))
        {
            var value = dict["hello"];
            System.Console.WriteLine($"Found={value}");
        }
        string? name = "Mist";
        if (name.IsString)
        {
            string s = name.AsString;
            System.Console.WriteLine($"Name={s}");
        }
        string? empty = null;
        if (empty.IsString)
        {
            string e = empty.AsString;
            System.Console.WriteLine($"Unreachable={e}");
        }
        else
        {
            System.Console.WriteLine("Value=void");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
