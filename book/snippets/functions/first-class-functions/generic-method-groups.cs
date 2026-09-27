using Standard;

namespace Snippet;

public class Program
{
    public static T Same<T>(T value)
    {
        return value;
    }

    public static string Pick__Standard_Int32__Standard_String(Union_Int32_String value)
    {
        {
            if (value.AsInt32OrNull is int left)
            {
                return System.FormattableString.Invariant($"T {left}");
            }
        }
        return "R";
    }

    public static int Apply(System.Func<int, int> f, int value)
    {
        return f(value);
    }

    public static void Main(string[] args)
    {
        System.Func<int, int> same = Same<int>;
        System.Func<Union_Int32_String, string> pick = Pick__Standard_Int32__Standard_String;
        System.Console.WriteLine(System.FormattableString.Invariant($"same(7)={same(7)} pick(5)={pick(5)} pick(w)={pick("w")} applied={Apply(Same<int>, 3)}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
