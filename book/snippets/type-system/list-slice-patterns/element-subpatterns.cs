using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var numbers = new[] { 10, 20, 30 };
        {
            if (numbers is [> 0, var _, var third])
            {
                Console.WriteLine($"third={third}");
            }
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
