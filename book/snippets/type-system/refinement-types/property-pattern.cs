namespace RefinementTypes;

public class Range
{
    public int Start;
    public int End;

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
        Range r = new Range(10, 50);
        if (r.Start >= 0)
        {
            Range valid = r;
            Console.WriteLine($"Valid: Start={valid.Start}");
        }
    }
}
