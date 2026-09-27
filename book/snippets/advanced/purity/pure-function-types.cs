using Standard;

namespace PurityFunctions;

public class Program
{
    public static int Twice(System.Func<int, int> f, int x)
    {
        return f(f(x));
    }

    public static int AddTen(int n)
    {
        return n + 10;
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Twice(n => n + 1, 3)={Twice((int n) => n + 1, 3)}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"Twice(AddTen, 1)={Twice(AddTen, 1)}"));
        int offset = 100;
        System.Func<int, int> shift = (int n) => n + offset;
        System.Console.WriteLine(System.FormattableString.Invariant($"Twice(shift, 1)={Twice(shift, 1)}"));
        System.Func<int, int> plain = shift;
        System.Console.WriteLine(System.FormattableString.Invariant($"plain(1)={plain(1)}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
