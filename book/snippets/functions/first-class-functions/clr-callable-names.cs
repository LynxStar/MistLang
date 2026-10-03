using Standard;

namespace Snippet;

public class Program
{
    public static void Main()
    {
        System.Func<int, int> increment = (int x) => x + 1;
        System.Func<int> answer = () => 42;
        System.Action<int> print = (int x) => System.Console.WriteLine(x.ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Action done = () => System.Console.WriteLine("done");
        print(increment(1));
        print(answer());
        done();
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
