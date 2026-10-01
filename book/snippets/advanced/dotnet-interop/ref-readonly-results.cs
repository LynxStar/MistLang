using Standard;
using System.Collections.Immutable;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Collections.Immutable.ImmutableArray<int>.Builder builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<int>();
        builder.Add(1);
        builder.Add(2);
        ref readonly int first = ref builder.ItemRef(0);
        int copied = builder.ItemRef(0);
        builder[0] = 10;
        System.Console.WriteLine(System.FormattableString.Invariant($"first={first}, copied={copied}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
