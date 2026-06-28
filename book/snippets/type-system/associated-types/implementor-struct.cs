using System;

namespace Snippet;

public interface IIterator<TCursor>
    where TCursor : struct
{
    TCursor CreateCursor(int pos);
}
public class RangeIterator : IIterator<RangeIterator.Cursor>
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
