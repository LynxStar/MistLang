using System;

namespace Snippet;

public class Point
{
    private int x;
    private int y;

    public Point(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public Point() : this(0, 0)
    {
    }

    private Point(Mist.Emission.CSharp.__DefaultInit marker)
    {
        this.x = default(int);
        this.y = default(int);
    }

    public static Point __CreateDefault()
    {
        return new Point(default(Mist.Emission.CSharp.__DefaultInit));
    }
}
