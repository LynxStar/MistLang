using Standard;

namespace Snippet;

public class Program
{
    private static int[] GetDefaults()
    {
        return new int[] { 10, 20, 30 };
    }

    public static void Main(string[] args)
    {
        int[] values = GetDefaults();
        System.Console.WriteLine($"Count: {values.Length}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
