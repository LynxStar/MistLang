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

    public Snippet.Point Translate(int dx, int dy)
    {
        return new Snippet.Point(this.x + dx, this.y + dy);
    }

    public string Describe()
    {
        return $"Point({this.x}, {this.y})";
    }
}
