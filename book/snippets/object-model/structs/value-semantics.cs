using System;

using Snippet;

Snippet.Color a = new Snippet.Color(255, 0, 0);
Snippet.Color b = a;
b.r = 0;
Console.WriteLine($"a.r={a.r} b.r={b.r}");
Snippet.Point p1 = new Snippet.Point(3, 4);
Snippet.Point p2 = p1;

namespace Snippet
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Color
    {
        public int r;
        public int g;
        public int b;

        public Color(int r, int g, int b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }

        private Color(int p0, int p1, int p2, Mist.Emission.CSharp.__StructInit marker)
        {
            this.r = p0;
            this.g = p1;
            this.b = p2;
        }

        internal static Color __FromParts(int p0, int p1, int p2, Mist.Emission.CSharp.__StructInit marker)
        {
            return new Color(p0, p1, p2, default(Mist.Emission.CSharp.__StructInit));
        }

        public int Brightness()
        {
            return (this.r + this.g + this.b) / 3;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            this.X = x;
            this.Y = y;
        }

        private Point(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
        {
            this.X = p0;
            this.Y = p1;
        }

        internal static Point __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
        {
            return new Point(p0, p1, default(Mist.Emission.CSharp.__StructInit));
        }
    }
}
