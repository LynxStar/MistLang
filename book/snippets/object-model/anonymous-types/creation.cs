using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var lang = new { Name = "Mist", Version = 1 };
        System.Console.WriteLine($"Name={lang.Name} Version={lang.Version}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
