using Standard;

namespace Snippet;

public class Program
{
    public static void Below(int i, int n)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"{i} < {n}"));
    }

    public static void Ordered(double left, double right)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"{left} < {right}"));
    }

    public static void Select(int i, int n)
    {
        bool ok = i < n;
        if (ok)
        {
            Below(i, n);
        }
    }

    public static void Compare(double left, double right)
    {
        if (left < right)
        {
            Ordered(left, right);
        }
    }

    public static void Main(string[] args)
    {
        Select(2, 5);
        Compare(-2.0, 3.0);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
