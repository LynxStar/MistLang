using Standard;

namespace Snippet;

public class Point
{
    private int x;
    private int y;

    public Point(int x, int y)
    {
        this.x = default(int);
        this.y = default(int);
        this.x = x;
        this.y = y;
    }
}
