using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int? maybeInt = default;
        Console.WriteLine($"maybeInt={(object?)maybeInt ?? "Void"}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
