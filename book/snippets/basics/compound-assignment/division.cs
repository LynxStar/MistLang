using Standard;

namespace Snippet;

public class Program
{
    public static int Divisor()
    {
        return 5;
    }

    public static void Main(string[] args)
    {
        int pool = 90;
        int divisor = Divisor();
        if (divisor > 0)
        {
            pool /= divisor;
        }
        System.Console.WriteLine(System.FormattableString.Invariant($"pool is {pool}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
