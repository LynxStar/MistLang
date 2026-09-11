using System;

namespace Snippet;

public class Rectangle
{
    public Rectangle(int width, int height)
    {
        this.Width = width;
        this.Height = height;
    }

    public int Area
    {
        get => Width * Height;
    }

    public int Width
    {
        get => field;
    }

    public int Height
    {
        get => field;
        set => field = value;
    }
}
