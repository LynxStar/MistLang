using System;

namespace Demo;

public class Rectangle
{
    private int _width;
    private int _height;

    public Rectangle(int width, int height)
    {
        this._width = width;
        this._height = height;
    }

    public int Area => _width * _height;

    public int Width
    {
        get => _width;
    }

    public int Height
    {
        get => _height;
        set => _height = value;
    }
}
