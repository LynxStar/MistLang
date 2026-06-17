using System;

namespace Snippet;

interface IIterator<TCursor>
    where TCursor : struct
{
    TCursor CreateCursor(int pos);
}
class RangeIterator : IIterator<RangeIterator.Cursor>
{
    public struct Cursor
    {
        public int Position;

        public Cursor(int pos)
        {
            Position = pos;
        }
    }

    public Cursor CreateCursor(int pos)
    {
        return new Cursor(pos);
    }

    public static RangeIterator __CreateDefault()
    {
        var inst = new RangeIterator();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var iter = new RangeIterator();
        var cursor = iter.CreateCursor(42);
        Console.WriteLine($"cursor.Position={cursor.Position}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
