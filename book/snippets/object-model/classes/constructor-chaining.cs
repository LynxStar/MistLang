using Standard;
using System;

namespace Demo;

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

    public static Point __CreateDefault()
    {
        var inst = new Point();
        inst.x = 0;
        inst.y = 0;
        return inst;
    }
}
