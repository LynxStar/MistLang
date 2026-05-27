using Standard;
using System;

namespace Snippet;

class Countdown : System.Collections.Generic.IEnumerable<int>
{
    public struct Enumerator : System.Collections.Generic.IEnumerator<int>
    {
        private int current;

        public Enumerator(int start)
        {
            current = start + 1;
        }

        public bool MoveNext()
        {
            current = current - 1;
            return current >= 0;
        }

        public int Current => current;

        // CLR IEnumerator<T> boilerplate
        object System.Collections.IEnumerator.Current => Current;
        public void Reset() { throw new System.NotSupportedException(); }
        public void Dispose() { }

        public static Enumerator __CreateDefault()
        {
            var inst = new Enumerator();
            inst.current = 0;
            return inst;
        }
    }

    private int start;

    public Countdown(int start)
    {
        this.start = start;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(start);
    }

    // CLR IEnumerable<T> boilerplate
    System.Collections.Generic.IEnumerator<int> System.Collections.Generic.IEnumerable<int>.GetEnumerator() => GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    protected Countdown()
    {
        this.start = 0;
    }

    public static Countdown __CreateDefault()
    {
        var inst = new Countdown();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var countdown = new Countdown(3);
        foreach (int n in countdown)
        {
            System.Console.WriteLine($"T-{n}");
        }
        System.Console.WriteLine("Liftoff!");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
