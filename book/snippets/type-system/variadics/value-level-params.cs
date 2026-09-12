using Standard;

namespace Snippet;

public class Program
{
    public static int SumAll(params int[] xs)
    {
        int total = 0;
        foreach (int x in xs)
        {
            total = total + x;
        }
        return total;
    }

    public static void Main(string[] cmdArgs)
    {
        int sum = SumAll(1, 2, 3, 4);
        System.Console.WriteLine($"sum={sum}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
