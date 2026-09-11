using System;

namespace Snippet;

public class Rectangle
{
    public Rectangle(int width, int height)
    {
        this.Width = width;
        this.Height = height;
    }

    public int Width { get; }

    public int Height { get; }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Rectangle rect = new Snippet.Rectangle(4, 4);
        if (rect is { Width: >= 1 and <= 10, Height: >= 1 and <= 10 })
        {
            Console.WriteLine("Small rectangle (both sides 1-10)");
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
