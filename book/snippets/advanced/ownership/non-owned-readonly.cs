using Standard;

namespace Snippet;

public class Counter
{
    public int Value;

    public Counter(int value)
    {
        this.Value = default(int);
        this.Value = value;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Counter({Value})");
    }

    public void Increment()
    {
        this.Value = Value + 1;
    }
}

public class NonOwnedReadonlyDemo
{
    public static void Run()
    {
        Snippet.Counter c = new Snippet.Counter(10);
        System.Console.WriteLine($"readonly view={c.Describe()}");
    }

    public NonOwnedReadonlyDemo()
    {
    }

    public static NonOwnedReadonlyDemo __CreateDefault()
    {
        return new NonOwnedReadonlyDemo();
    }
}
