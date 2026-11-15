using Standard;

namespace Snippet;

public class Program
{
    public static void Needs(int low, int high)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"{low} < {high}"));
    }

    public static void Pick(bool small)
    {
        int low;
        int high;
        if (small)
        {
            low = 1;
            high = 2;
        }
        else
        {
            low = 100;
            high = 200;
        }
        Needs(low, high);
    }

    public static void Main(string[] args)
    {
        Pick(true);
        Pick(false);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
