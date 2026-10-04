using Standard;

namespace Snippet;

public class Program
{
    private static string Describe(object value)
    {
        return value switch { int[] { Length: 3 } three => System.FormattableString.Invariant($"three elements, first {three[0]}"), int[] other => "an array of another length", var _ => "not an int array" };
    }

    public static void Main(string[] args)
    {
        object five = new int[5];
        {
            if (five is int[] { Length: 3 } three)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"matched {three.Length}"));
            }
            else
            {
                System.Console.WriteLine("five elements are not an int[3]");
            }
        }
        System.Console.WriteLine(Describe(new int[3]));
        System.Console.WriteLine(Describe(five));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
