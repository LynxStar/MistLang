using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int counter = 0;
        System.Func<int> increment = () => 
        {
            counter = counter + 1;
            return counter;
        }
;
        System.Console.WriteLine(System.FormattableString.Invariant($"increment()={increment()}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"increment()={increment()}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"counter={counter}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
