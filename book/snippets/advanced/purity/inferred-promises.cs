using Standard;

namespace PurityInference;

public class Vec
{
    public int X;

    public Vec(int x)
    {
        X = x;
    }
}

public class Holder
{
    public PurityInference.Vec Origin = new PurityInference.Vec(7);

    public Holder()
    {
    }

    public static Holder __CreateDefault()
    {
        return new Holder();
    }
}

public class Interval
{
    public readonly int Start;
    public readonly int End;

    public Interval(int start, int end)
    {
        Start = start;
        End = end;
    }

    public int Length
    {
        get
        {
            return End - Start;
        }
    }
}

public class Program
{
    public static int Square(int n)
    {
        return n * n;
    }

    public static int SumOfSquares(int a, int b)
    {
        return Square(a) + Square(b);
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"SumOfSquares(3, 4)={SumOfSquares(3, 4)}"));
        PurityInference.Holder first = PurityInference.Holder.__CreateDefault();
        PurityInference.Holder second = PurityInference.Holder.__CreateDefault();
        first.Origin.X = 70;
        System.Console.WriteLine(System.FormattableString.Invariant($"first.Origin.X={first.Origin.X} second.Origin.X={second.Origin.X}"));
        PurityInference.Interval span = new PurityInference.Interval(3, 8);
        {
            if (span is PurityInference.Interval and { Length: > 0 } and var proven)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"proven.Length={proven.Length}"));
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
