using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] numbers = new int[] { 10, 20, 30, 40, 50 };
        int[] empty = numbers[0..0];
        if (empty is [])
        {
            System.Console.WriteLine("empty matched");
        }
        if (numbers is [])
        {
            System.Console.WriteLine("unreachable");
        }
        else
        {
            System.Console.WriteLine("numbers is not empty");
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
