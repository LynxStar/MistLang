using Standard;

namespace Snippet;

public class Program
{
    public static void Fill(System.Span<int> items, int n)
    {
        int total = 0;
        for (int i = 0; i < n; i++)
        {
            items[i] = i;
            total += i;
        }
        System.Console.WriteLine(System.FormattableString.Invariant($"Filled {n}, total {total}"));
    }

    public static void Main(string[] args)
    {
        System.Span<int> items = stackalloc int[4];
        Fill(items, 4);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
