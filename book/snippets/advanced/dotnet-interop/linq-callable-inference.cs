using Standard;
using System.Linq;

namespace Snippet;

public class Program
{
    public static void Main()
    {
        int[] numbers = new int[] { 1, 2, 3 };
        System.Collections.Generic.IEnumerable<int> mapped = numbers.Select<int, int>((int n) => n * 2);
        System.Collections.Generic.IEnumerable<int> kept = numbers.Where((int n) => n > 1);
        foreach (int value in mapped)
        {
            System.Console.WriteLine(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        foreach (int value in kept)
        {
            System.Console.WriteLine(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
