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

    public int DistanceFromOrigin()
    {
        return System.Math.Abs(this.x) + System.Math.Abs(this.y);
    }

    public static void Main(string[] args)
    {
        Snippet.Point p = new Snippet.Point(3, 4);
        System.Console.WriteLine(System.FormattableString.Invariant($"Distance: {p.DistanceFromOrigin()}"));
    }
}
