using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int temperature = 72;
        if (temperature is >= 60 and <= 80)
        {
            System.Console.WriteLine(System.FormattableString.Invariant($"Temperature {temperature} is comfortable"));
        }
        int value = 15;
        if (value is < 0 or > 10)
        {
            System.Console.WriteLine(System.FormattableString.Invariant($"Value {value} is outside 0-10 range"));
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
