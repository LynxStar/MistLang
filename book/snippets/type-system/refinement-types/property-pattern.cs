using System;

namespace Snippet;

public class Range
{
    public readonly int Start;

    public readonly int End;

    public Range(int start, int end)
    {
        this.Start = start;
        this.End = end;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var r = new Range(10, 50);
        if (r.Start >= 0)
        {
            Range valid = r;
            Console.WriteLine($"Valid: Start={valid.Start}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
