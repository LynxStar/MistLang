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

    public int Width { get => _width; }

    public int Height { get => _height; set => _height = value; }

    protected Rectangle()
    {
        this._width = 0;
        this._height = 0;
    }

    public static Rectangle __CreateDefault()
    {
        var inst = new Rectangle();
        return inst;
    }
}
