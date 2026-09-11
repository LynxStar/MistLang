using System;

namespace Snippet;

public class Program
{
    private static void PrintUserId(int id)
    {
        Console.WriteLine(id);
    }

    public static void Main(string[] args)
    {
        int aliasedId = 100;
        int rawId = aliasedId;
        int plainInt = 7;
        Snippet.Program.PrintUserId(plainInt);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
