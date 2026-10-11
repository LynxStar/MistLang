using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        object five = new int[5];
        int[]? three = (five is int[] { Length: 3 } __as168 ? __as168 : default(int[]?));
        System.Console.WriteLine(three is null);
        object pair = (new int[3], "label");
        (int[], string)? found = (pair is System.ValueTuple<int[], string> { Item1: { Length: 3 } } __as358 ? __as358 : default((int[], string)?));
        {
            if (found is System.ValueTuple<int[], string> { Item1: { Length: 3 } } labelled)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"{labelled.Item2}: {labelled.Item1.Length}"));
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
