using Standard;
using System;

namespace AssociatedTypesDemo;

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

        public static Cursor __CreateDefault()
        {
            var inst = new Cursor();
            inst.Position = 0;
            return inst;
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
        System.Console.WriteLine($"cursor.Position={cursor.Position}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
