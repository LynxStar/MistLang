using Standard;

namespace Snippet;

public class Program
{
    public static int Replacement()
    {
        return 7;
    }

    public static void NeedsTen(int value)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Still ten: {value}"));
    }

    public static void Main(string[] args)
    {
        int x = 5;
        int y = x * 2;
        x = Replacement();
        NeedsTen(y);
        System.Console.WriteLine(System.FormattableString.Invariant($"x is now {x}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
