using Standard;

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

    public string Describe()
    {
        return System.FormattableString.Invariant($"Point({x}, {y})");
    }

    public Snippet.Point Translate(int dx, int dy)
    {
        return new Snippet.Point(this.x + dx, this.y + dy);
    }

    public static void Main(string[] args)
    {
        Snippet.Point p = new Snippet.Point(3, 4);
        System.Console.WriteLine(p.Describe());
        Snippet.Point moved = p.Translate(10, 20);
        System.Console.WriteLine(moved.Describe());
    }
}
