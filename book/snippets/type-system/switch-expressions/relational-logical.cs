using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int temp = 75;
        string comfort = temp switch { < 32 => "Freezing", < 60 => "Cold", <= 80 => "Comfortable", var _ => "Hot" };
        System.Console.WriteLine(System.FormattableString.Invariant($"Temperature {temp} => {comfort}"));
        int score = 85;
        string grade = score switch { >= 90 and <= 100 => "A", >= 80 and < 90 => "B", >= 70 and < 80 => "C", >= 60 and < 70 => "D", var _ => "F" };
        System.Console.WriteLine(System.FormattableString.Invariant($"Score {score} => Grade {grade}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
