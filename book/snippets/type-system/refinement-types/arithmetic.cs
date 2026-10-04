using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int a = 20;
        int sum = a + 5;
        int diff = a - 2;
        int negated = -a;
        bool yes = true;
        bool opposite = !yes;
        System.Console.WriteLine(negated.ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(opposite);
        int computed = a + 10;
        {
            if (computed is int and >= 18 and var verified)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"Still adult: {verified}"));
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
