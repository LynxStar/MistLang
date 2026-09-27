using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] numbers = new int[] { 10, 20, 30, 40, 50 };
        {
            if (numbers is [var first, .. var middle, var last])
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"first={first}"));
                System.Console.WriteLine(System.FormattableString.Invariant($"last={last}"));
                System.Console.WriteLine(System.FormattableString.Invariant($"middleLength={middle.Length}"));
                System.Console.WriteLine(System.FormattableString.Invariant($"middleFirst={middle[0]}"));
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
