namespace Snippet;

public struct Point
{
    public int X;

    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }
}

public struct Point3D
{
    public int X;  // flattened from Point

    public int Y;  // flattened from Point

    public int Z;

    public Point3D(int x, int y, int z)
    {
        this.X = x;
        this.Y = y;
        this.Z = z;
    }

    public static implicit operator Point(Point3D value)
    {
        return new Point { X = value.X, Y = value.Y };
    }
}
