using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? maybeInt = null;
        Console.WriteLine($"maybeInt={(object?)maybeInt ?? "Void"}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
