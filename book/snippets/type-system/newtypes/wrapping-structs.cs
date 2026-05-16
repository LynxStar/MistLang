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
