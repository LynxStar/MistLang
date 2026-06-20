using System;
using System.IO;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var memory = new MemoryStream();
        memory.WriteByte(65);
        memory.WriteByte(66);
        Stream stream = memory;
        System.Console.WriteLine($"length={stream.Length}");
        System.Console.WriteLine($"canRead={stream.CanRead}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
