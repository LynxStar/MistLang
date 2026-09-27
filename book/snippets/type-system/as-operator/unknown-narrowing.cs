using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        object box = 99;
        int? maybeInt = (box is int __as159 ? __as159 : default(int?));
        {
            if (maybeInt is int n)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"Got int: {n}"));
            }
            else
            {
                System.Console.WriteLine("Not an int");
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
