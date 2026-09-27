using Standard;

namespace PurityCulture;

public class Program
{
    public static string Shout(string s)
    {
        return s.ToUpperInvariant();
    }

    public static string Price(double amount)
    {
        return System.FormattableString.Invariant($"{amount} EUR");
    }

    public static int Parsed(string digits)
    {
        return System.Convert.ToInt32(digits, System.Globalization.CultureInfo.InvariantCulture) + 1;
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine($"Shout(istanbul)={Shout("istanbul")}");
        System.Console.WriteLine($"Price(1.5)={Price(1.5)}");
        System.Console.WriteLine(System.FormattableString.Invariant($"Parsed(41)={Parsed("41")}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
