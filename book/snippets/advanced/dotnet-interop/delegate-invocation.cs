using Standard;

namespace Snippet;

public class Program
{
    public static void Main()
    {
        System.Predicate<int> isEven = new System.Predicate<int>((int x) => x % 2 == 0);
        bool result = isEven(4);
        System.Console.WriteLine(result);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
