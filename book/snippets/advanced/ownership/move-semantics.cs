using System;

namespace Snippet;

public class Handle
{
    public int Id;

    public Handle(int id)
    {
        this.Id = id;
    }

    public string Describe()
    {
        return $"Handle({Id})";
    }
}

public class MoveDemo
{
    public static void Consume(Snippet.Handle h)
    {
        Console.WriteLine($"consumed={h.Describe()}");
    }

    public static void Run()
    {
        Snippet.Handle h1 = new Snippet.Handle(10);
        Console.WriteLine($"before move={h1.Describe()}");
        Snippet.Handle h2 = h1;
        Console.WriteLine($"after move to h2={h2.Describe()}");
        Consume(h2);
        Console.WriteLine($"transfer complete");
    }

    public MoveDemo()
    {
    }

    public static MoveDemo __CreateDefault()
    {
        return new MoveDemo();
    }
}
