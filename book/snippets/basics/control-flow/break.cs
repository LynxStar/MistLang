using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        for (int i = 0; i < 10; i = i + 1)
        {
            if (i == 3)
            {
                break;
            }
            System.Console.WriteLine(System.FormattableString.Invariant($"i = {i}"));
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
