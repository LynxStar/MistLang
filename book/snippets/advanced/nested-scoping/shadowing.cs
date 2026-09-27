using Standard;

namespace Snippet;

public class ShadowingDemo
{
    public static void Main(string[] args)
    {
        int x = 10;
        System.Console.WriteLine(System.FormattableString.Invariant($"outer x = {x}"));
        {
            int x_1 = 99;
            System.Console.WriteLine(System.FormattableString.Invariant($"inner x = {x_1}"));
        }
        System.Console.WriteLine(System.FormattableString.Invariant($"outer x after block = {x}"));
    }

    public ShadowingDemo()
    {
    }

    public static ShadowingDemo __CreateDefault()
    {
        return new ShadowingDemo();
    }
}
