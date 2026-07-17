using System;

namespace Snippet;

public class Point
{
    public int X;

    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }
}
public class Point3D : Point
{
    public int Z;

    public Point3D(int x, int y, int z) : base(x, y)
    {
        this.Z = z;
    }
}
