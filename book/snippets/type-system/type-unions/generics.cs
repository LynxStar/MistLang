using Standard;

namespace Snippet;

internal class Box<T>
{
    public T Value;

    public Box(T value)
    {
        this.Value = value;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Box<Union_Int32_String> box = new Snippet.Box<Union_Int32_String>(42);
        Union_Int32_String ok = 100;
        Union_Int32_String err = "not found";
        {
            if (ok.AsInt32OrNull is int n)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"ok: {n}"));
            }
        }
        {
            if (err.AsStringOrNull is string s)
            {
                System.Console.WriteLine($"err: {s}");
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
