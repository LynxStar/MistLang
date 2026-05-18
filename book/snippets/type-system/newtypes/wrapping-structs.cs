namespace Newtypes;

public struct Point
{
    public int X;

    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public readonly string Describe()
    {
        return $"({this.X}, {this.Y})";
    }

    public static Point __CreateDefault()
    {
        var inst = new Point();
        inst.X = 0;
        inst.Y = 0;
        return inst;
    }
}
public struct Position
{
    public int X;

    public int Y;

    public Position(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public readonly string Describe()
    {
        return $"({this.X}, {this.Y})";
    }
}
