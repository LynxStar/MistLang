using System;

namespace Snippet;

class Countdown : System.Collections.Generic.IEnumerable<int>
{
    private int start;

    public Countdown(int start)
    {
        this.start = start;
    }

    public Snippet.Countdown.Enumerator GetEnumerator()
    {
        return new Snippet.Countdown.Enumerator(start);
    }

    System.Collections.Generic.IEnumerator<int> System.Collections.Generic.IEnumerable<int>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Enumerator : System.Collections.Generic.IEnumerator<int>
    {
        private int current;

        public Enumerator(int start)
        {
            current = start + 1;
        }

        private Enumerator(int p0, Mist.Emission.CSharp.__StructInit marker)
        {
            this.current = p0;
        }

        internal static Enumerator __FromParts(int p0, Mist.Emission.CSharp.__StructInit marker)
        {
            return new Enumerator(p0, default(Mist.Emission.CSharp.__StructInit));
        }

        public int Current
        {
            get => current;
        }

        public bool MoveNext()
        {
            current = current - 1;
            return current >= 0;
        }

        object System.Collections.IEnumerator.Current => Current;

        public void Reset() { throw new System.NotSupportedException(); }

        public void Dispose() { }
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Countdown countdown = new Snippet.Countdown(3);
        foreach (int n in countdown)
        {
            Console.WriteLine($"T-{n}");
        }
        Console.WriteLine("Liftoff!");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
