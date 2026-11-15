using Standard;

namespace Snippet;

public class Program
{
    public static void Admit(int userInput)
    {
        {
            if (userInput is int and >= 18 and var validAge)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"Valid adult age: {validAge}"));
            }
            else
            {
                System.Console.WriteLine("Not an adult age");
            }
        }
    }

    public static void Main(string[] args)
    {
        int known = 20;
        Admit(25);
        Admit(10);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
