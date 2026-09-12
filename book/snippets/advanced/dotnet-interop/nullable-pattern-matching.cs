using Standard;
using System.Collections.Generic;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Collections.Generic.Dictionary<string, int> dict = new System.Collections.Generic.Dictionary<string, int>();
        dict.Add("hello", 42);
        if (dict.ContainsKey("hello"))
        {
            int value = dict["hello"];
            System.Console.WriteLine($"Found={value}");
        }
        string name = "Mist";
        {
            if (name is string s)
            {
                System.Console.WriteLine($"Name={s}");
            }
        }
        string empty = null;
        {
            if (empty is string e)
            {
                System.Console.WriteLine($"Unreachable={e}");
            }
            else
            {
                System.Console.WriteLine("Value=void");
            }
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
