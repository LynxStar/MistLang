using System;

namespace Snippet;

public class Point
{
    private int x;

    private int y;

    protected Point()
    {
        this.x = 0;
        this.y = 0;
    }

    public static Point __CreateDefault()
    {
        var inst = new Point();
        return inst;
    }
}
