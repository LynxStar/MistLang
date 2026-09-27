using Standard;

namespace Snippet;

public class Program
{
    public static void Main()
    {
        System.Console.WriteLine(Describe(3));
        string Describe(int n)
        {
            if (IsEven(n))
            {
                return System.FormattableString.Invariant($"{n} is even");
            }
            return System.FormattableString.Invariant($"{n} is odd");
        }
        bool IsEven(int n)
        {
            return n % 2 == 0;
        }
        int calls = 0;
        void Count()
        {
            calls = calls + 1;
        }
        Count();
        Count();
        System.Console.WriteLine(System.FormattableString.Invariant($"calls={calls}"));
        static int Square(int x)
        {
            return x * x;
        }
        System.Console.WriteLine(Square(7).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
