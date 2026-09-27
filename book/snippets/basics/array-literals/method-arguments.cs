using Standard;

namespace Snippet;

public class Program
{
    private static void PrintAll(int[] items)
    {
        foreach (int item in items)
        {
            System.Console.WriteLine(item.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    public static void Main(string[] args)
    {
        PrintAll(new int[] { 10, 20, 30 });
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
