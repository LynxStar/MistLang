using Standard;

namespace Snippet;

public class Rectangle
{
    public Rectangle(int width, int height)
    {
        this.__Width_field = default(int);
        this.__Height_field = default(int);
        this.Width = width;
        this.Height = height;
    }

    public int Area
    {
        get => Width * Height;
    }

    private int __Width_field;
    public int Width
    {
        get => __Width_field;
        init => __Width_field = value;
    }

    private int __Height_field;
    public int Height
    {
        get => __Height_field;
        set => __Height_field = value;
    }
}
