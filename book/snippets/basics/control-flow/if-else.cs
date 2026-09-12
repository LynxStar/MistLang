using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int score = 85;
        if (score >= 90)
        {
            System.Console.WriteLine("Grade: A");
        }
        else         if (score >= 80)
        {
            System.Console.WriteLine("Grade: B");
        }
        else
        {
            System.Console.WriteLine("Grade: C or below");
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
