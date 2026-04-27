using System;

namespace HelloMist;

public struct Color
{
    public int r;

    public int g;

    public int b;

    public Color(int r)
    {
        this.r = r;
        this.g = g;
        this.b = b;
    }

    public string ToHex()
    {
        return $"#{r}{g}{b}";
    }
}
