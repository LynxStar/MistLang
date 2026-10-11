using Standard;

namespace Snippet;

public class Handle
{
    public int Id;

    public Handle(int id)
    {
        this.Id = default(int);
        this.Id = id;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Handle({Id})");
    }
}

public class MoveDemo
{
    public static void Consume(Snippet.Handle h)
    {
        System.Console.WriteLine($"consumed={h.Describe()}");
    }

    public static void Run()
    {
        Snippet.Handle h1 = new Snippet.Handle(10);
        System.Console.WriteLine($"before move={h1.Describe()}");
        Snippet.Handle h2 = h1;
        System.Console.WriteLine($"after move to h2={h2.Describe()}");
        Consume(h2);
        System.Console.WriteLine($"transfer complete");
    }

    public MoveDemo()
    {
    }

    public static MoveDemo __CreateDefault()
    {
        return new MoveDemo();
    }
}
