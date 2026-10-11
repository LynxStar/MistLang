using Standard;

using Snippet;

System.ReadOnlySpan<int> ro = stackalloc int[3];
System.Console.WriteLine(System.FormattableString.Invariant($"ro.Length={ro.Length}"));
System.Console.WriteLine(System.FormattableString.Invariant($"ro[0]={ro[0]}"));
System.Span<Snippet.Point> points = stackalloc Snippet.Point[2];
points[0] = new Snippet.Point(1, 2);
points[1] = new Snippet.Point(3, 4);
System.Console.WriteLine(System.FormattableString.Invariant($"points[0].x={points[0].x}"));
System.Console.WriteLine(System.FormattableString.Invariant($"points[1].y={points[1].y}"));

namespace Snippet
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Point
    {
        public int x;
        public int y;

        public Point(int a, int b)
        {
            this.x = default(int);
            this.y = default(int);
            this.x = a;
            this.y = b;
        }

        private Point(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
        {
            this.x = p0;
            this.y = p1;
        }

        internal static Point __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
        {
            return new Point(p0, p1, default(Mist.Emission.CSharp.__StructInit));
        }
    }
}
