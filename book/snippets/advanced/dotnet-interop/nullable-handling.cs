using System;
using System.Collections.Generic;

namespace DotnetInteropSnippets;

public class Program
{
    public static void Main()
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var dict = new Dictionary<string, int>();
        dict.Add("answer", 42);
        if (dict.ContainsKey("answer"))
        {
            var value = dict["answer"];
            System.Console.WriteLine($"Found={value}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
