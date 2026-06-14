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
}
