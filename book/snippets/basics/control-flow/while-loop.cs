using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int count = 0;
        while (count < 3)
        {
            System.Console.WriteLine($"Count: {count}");
            count = count + 1;
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
