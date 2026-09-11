using System;

namespace Snippet;

public class Point
{
    private int x;
    private int y;

    public Point()
    {
        this.x = default(int);
        this.y = default(int);
    }

    public static Point __CreateDefault()
    {
        return new Point();
    }
}
