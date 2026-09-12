using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        object maybeInt = 17;
        {
            if (maybeInt is int narrowed)
            {
                int doubled = narrowed + narrowed;
                System.Console.WriteLine($"narrowed int doubled: {doubled}");
            }
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
