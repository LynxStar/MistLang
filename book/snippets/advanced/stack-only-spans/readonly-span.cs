using Snippet;
using System;

System.ReadOnlySpan<int> ro = stackalloc int[3];
System.Console.WriteLine($"ro.Length={ro.Length}");
System.Console.WriteLine($"ro[0]={ro[0]}");
System.Span<Point> points = stackalloc Point[2];
points[0] = new Point(1, 2);
points[1] = new Point(3, 4);
System.Console.WriteLine($"points[0].x={points[0].x}");
System.Console.WriteLine($"points[1].y={points[1].y}");

namespace Snippet
{
    public struct Point
    {
        public int x;

        public int y;

        public Point(int a, int b)
        {
            this.x = a;
            this.y = b;
        }
    }
}
