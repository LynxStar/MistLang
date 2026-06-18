using System;

namespace Snippet;

public struct Vector2
{
    public int X;

    public int Y;

    public static Vector2 __CreateDefault()
    {
        var inst = new Vector2();
        inst.X = 0;
        inst.Y = 0;
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var counts = new int[4];
        System.Console.WriteLine($"counts[0]={counts[0]}");
        var positions = new Vector2[3];
        System.Console.WriteLine($"positions[0].X={positions[0].X}");
        System.Console.WriteLine($"positions[1].Y={positions[1].Y}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
