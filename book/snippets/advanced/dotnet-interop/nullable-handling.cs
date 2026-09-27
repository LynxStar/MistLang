using Standard;
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
            System.Console.WriteLine(System.FormattableString.Invariant($"Found={value}"));
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
