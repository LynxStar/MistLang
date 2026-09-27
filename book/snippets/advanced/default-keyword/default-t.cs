using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int i = default(int);
        bool b = default(bool);
        string s = "";
        System.Console.WriteLine(System.FormattableString.Invariant($"int={i} bool={b} string={s}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
