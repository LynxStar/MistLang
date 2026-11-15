using Standard;

namespace Snippet;

public class Counter
{
    public int Total { get; set; }

    public Counter()
    {
        this.Total = default(int);
    }

    public static Counter __CreateDefault()
    {
        return new Counter();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        int total = 100;
        total -= 10;
        total *= 2;
        total /= 4;
        total %= 7;
        System.Console.WriteLine(System.FormattableString.Invariant($"total is {total}"));
        string text = "a";
        text += "b";
        text += 3.ToString(System.Globalization.CultureInfo.InvariantCulture);
        System.Console.WriteLine($"text is {text}");
        Snippet.Counter counter = new Snippet.Counter();
        counter.Total += 7;
        counter.Total *= 2;
        System.Console.WriteLine(System.FormattableString.Invariant($"Total is {counter.Total}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
