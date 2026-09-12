using Standard;

namespace Snippet;

public interface IIterator<Cursor>
    where Cursor : struct
{
    Cursor CreateCursor(int pos);
}

internal class RangeIterator : Snippet.IIterator<Snippet.RangeIterator.Cursor>
{
    public Snippet.RangeIterator.Cursor CreateCursor(int pos)
    {
        return new Snippet.RangeIterator.Cursor(pos);
    }

    public RangeIterator()
    {
    }

    public static RangeIterator __CreateDefault()
    {
        return new RangeIterator();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Cursor
    {
        public int Position;

        public Cursor(int pos)
        {
            Position = pos;
        }

        private Cursor(int p0, Mist.Emission.CSharp.__StructInit marker)
        {
            this.Position = p0;
        }

        internal static Cursor __FromParts(int p0, Mist.Emission.CSharp.__StructInit marker)
        {
            return new Cursor(p0, default(Mist.Emission.CSharp.__StructInit));
        }
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.RangeIterator iter = new Snippet.RangeIterator();
        Snippet.RangeIterator.Cursor cursor = iter.CreateCursor(42);
        System.Console.WriteLine($"cursor.Position={cursor.Position}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
