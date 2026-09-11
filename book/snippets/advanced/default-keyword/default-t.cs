using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int i = default(int);
        bool b = default(bool);
        string s = "";
        Console.WriteLine($"int={i} bool={b} string={s}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
