using System;

using Snippet;

string message = "Hello from Mist!";
int total = 0;
Snippet.Point origin = new Snippet.Point(0, 0);

namespace Snippet
{
    public class Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}
