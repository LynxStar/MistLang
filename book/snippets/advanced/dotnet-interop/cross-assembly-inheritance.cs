using System;
using System.IO;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.IO.MemoryStream memory = new System.IO.MemoryStream();
        memory.WriteByte(65);
        memory.WriteByte(66);
        System.IO.Stream stream = memory;
        Console.WriteLine($"length={stream.Length}");
        Console.WriteLine($"canRead={stream.CanRead}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
