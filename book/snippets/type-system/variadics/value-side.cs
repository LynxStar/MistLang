using Standard;

namespace Snippet;

public class Program
{
    public static int Describe_3<T1, T2, T3>((T1, T2, T3) args)
    {
        return 3;
    }

    public static void Main(string[] cmdArgs)
    {
        int arity = Describe_3<int, string, bool>((1, "x", true));
        System.Console.WriteLine(System.FormattableString.Invariant($"arity={arity}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
