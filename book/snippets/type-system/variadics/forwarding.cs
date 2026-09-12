using Standard;

namespace Snippet;

public class Program
{
    public static TResult Apply_3<T1, T2, T3, TResult>(System.Func<T1, T2, T3, TResult> f, (T1, T2, T3) args)
    {
        return f(args.Item1, args.Item2, args.Item3);
    }

    public static void Main(string[] cmdArgs)
    {
        System.Func<int, int, int, int> add = (int a, int b, int c) => a + b + c;
        int result = Apply_3<int, int, int, int>(add, (2, 3, 4));
        System.Console.WriteLine($"forward={result}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
