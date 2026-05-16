using System;

namespace PatternDemo;

public class Rectangle
{
    public int Width { get; }
    public int Height { get; }

    public Rectangle(int width, int height)
    {
        this.Width = width;
        this.Height = height;
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
}
