using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Func<int, int> doubler = (int x) => x * 2;
        System.Func<int, int> square = (int x) => x * x;
        System.Console.WriteLine($"doubler(5)={doubler(5)}");
        System.Console.WriteLine($"square(4)={square(4)}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
