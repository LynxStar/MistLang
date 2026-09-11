using System;
using System.Collections.Generic;

namespace DotnetInteropSnippets;

public class Program
{
    public static void Main()
    {
        System.Collections.Generic.Dictionary<string, int> dict = new System.Collections.Generic.Dictionary<string, int>();
        dict.Add("answer", 42);
        if (dict.ContainsKey("answer"))
        {
            int value = dict["answer"];
            Console.WriteLine($"Found={value}");
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
