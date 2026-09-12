using Standard;

namespace Snippet;

public class Interval
{
    public readonly int Start;
    public readonly int End;

    public Interval(int start, int end)
    {
        this.Start = start;
        this.End = end;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Interval r = new Snippet.Interval(10, 50);
        {
            if (r is Snippet.Interval and { Start: >= 0 } and var valid)
            {
                System.Console.WriteLine($"Valid: Start={valid.Start}");
            }
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
