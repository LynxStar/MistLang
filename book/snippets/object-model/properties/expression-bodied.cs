using System;

namespace Snippet;

public class Rectangle
{
    public Rectangle(int width, int height)
    {
        this.Width = width;
        this.Height = height;
    }

    public int Area => Width * Height;

    private int _Width_k__BackingField;
    public int Width { get => _Width_k__BackingField; }

    private int _Height_k__BackingField;
    public int Height { get => _Height_k__BackingField; set => _Height_k__BackingField = value; }
}
