using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] triple = new int[] { 1, 2, 3 };
        if (triple is [1, 2, 3])
        {
            System.Console.WriteLine("matched exactly [1, 2, 3]");
        }
        int[] pair = new int[] { 1, 2 };
        if (pair is [1, 2, 3])
        {
            System.Console.WriteLine("unreachable");
        }
        else
        {
            System.Console.WriteLine("length mismatch: no match");
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
