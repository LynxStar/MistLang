using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int i = 5;
        int before = i++;
        int after = ++i;
        System.Console.WriteLine(System.FormattableString.Invariant($"i++ gave {before}, ++i gave {after}, i is {i}"));
        System.Collections.Generic.List<int> queue = new System.Collections.Generic.List<int>();
        queue.Add(10);
        queue.Add(20);
        int k = 0;
        int read = queue[k++];
        System.Console.WriteLine(System.FormattableString.Invariant($"queue[k++] read {read}, k is {k}"));
        int countdown = 3;
        while (countdown-- > 0)
        {
            System.Console.WriteLine(System.FormattableString.Invariant($"tick {countdown}"));
        }
        double half = 0.5;
        half++;
        System.Console.WriteLine(System.FormattableString.Invariant($"half is {half}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
