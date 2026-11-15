using Standard;

namespace Snippet;

public class Program
{
    public static void Narrow(int x)
    {
        if (x >= 18)
        {
            int narrowed = x;
            System.Console.WriteLine(System.FormattableString.Invariant($"Narrowed to AdultAge: {narrowed}"));
        }
    }

    public static void Validate(int userInput)
    {
        {
            if (userInput is int and >= 21 and var validAge)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"Valid drinking age: {validAge}"));
            }
        }
    }

    public static void Main(string[] args)
    {
        Narrow(25);
        Validate(21);
        int age = 30;
        int baseValue = age;
        System.Console.WriteLine(System.FormattableString.Invariant($"Widened to int: {baseValue}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
