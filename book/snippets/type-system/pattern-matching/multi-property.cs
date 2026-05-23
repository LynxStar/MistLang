using System;

namespace Snippet;

public class Rectangle
{
    public int Width { get; }

    public int Height { get; }

    public Rectangle(int width, int height)
    {
        this.Width = width;
        this.Height = height;
    }

    protected Rectangle()
    {
        this.Width = 0;
        this.Height = 0;
    }

    public static Rectangle __CreateDefault()
    {
        var inst = new Rectangle();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var rect = new Rectangle(4, 4);
        if (rect is { Width: >= 1 and <= 10, Height: >= 1 and <= 10 })
        {
            Console.WriteLine("Small rectangle (both sides 1-10)");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
