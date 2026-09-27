using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        (int, object) source = (7, "seven");
        (int num, object data) = source;
        System.Console.WriteLine(System.FormattableString.Invariant($"num={num}"));
        {
            if (data is string ds)
            {
                System.Console.WriteLine($"data narrowed to string: {ds}");
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
