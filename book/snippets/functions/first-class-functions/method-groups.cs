using Standard;

namespace Snippet;

public class Program
{
    public static int Negate(int x)
    {
        return -x;
    }

    public static async System.Threading.Tasks.Task<int> Fetch()
    {
        return 7;
    }

    public static async System.Threading.Tasks.Task<int> Read()
    {
        return await Fetch();
    }

    public static void Main(string[] args)
    {
        System.Func<int, int> fn = Negate;
        System.Console.WriteLine(System.FormattableString.Invariant($"fn(42)={fn(42)}"));
        System.Func<int> read = ((System.Func<int>)(() => Snippet.Program.Read().GetAwaiter().GetResult()));
        System.Console.WriteLine(System.FormattableString.Invariant($"read()={read()}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
