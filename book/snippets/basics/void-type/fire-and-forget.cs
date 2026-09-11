using System;

namespace Snippet;

public class Program
{
    public static void PrintMessage(string msg)
    {
        Console.WriteLine(msg);
    }

    public static void Main(string[] args)
    {
        PrintMessage("fire-and-forget call");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
