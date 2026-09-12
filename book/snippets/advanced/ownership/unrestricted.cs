using Standard;

namespace Snippet;

public class Counter
{
    public int Value;

    public Counter(int value)
    {
        this.Value = value;
    }

    public void Increment()
    {
        this.Value = Value + 1;
    }

    public string Describe()
    {
        return $"Counter({Value})";
    }
}

public class UnrestrictedDemo
{
    public static void Run()
    {
        Snippet.Counter c1 = new Snippet.Counter(10);
        Snippet.Counter c2 = c1;
        c2.Increment();
        System.Console.WriteLine($"c1={c1.Describe()}, c2={c2.Describe()}");
    }

    public UnrestrictedDemo()
    {
    }

    public static UnrestrictedDemo __CreateDefault()
    {
        return new UnrestrictedDemo();
    }
}
