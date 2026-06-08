using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Byte_Int32 childAge = 5;
        Union_Byte_Int32 byteVal = 50;
        Console.WriteLine($"childAge = {childAge}");
        Console.WriteLine($"byteVal = {byteVal}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
