using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String mixed = 123;
        string result = mixed switch { { AsInt32OrNull: int n } => System.FormattableString.Invariant($"integer: {n}"), { AsStringOrNull: string s } => $"string: {s}" };
        System.Console.WriteLine(result);
        Union_Int32_String mixed2 = "hello";
        string result2 = mixed2 switch { { AsInt32OrNull: int n } => System.FormattableString.Invariant($"integer: {n}"), { AsStringOrNull: string s } => $"string: {s}" };
        System.Console.WriteLine(result2);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
