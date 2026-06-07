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

    protected Handle()
    {
        this.Id = 0;
    }

    public static Handle __CreateDefault()
    {
        var inst = new Handle();
        return inst;
    }
}
public class MoveDemo
{
    public static void Consume(Handle h)
    {
        System.Console.WriteLine($"consumed={h.Describe()}");
    }

    public static void Run()
    {
        Handle h1 = new Handle(10);
        System.Console.WriteLine($"before move={h1.Describe()}");
        Handle h2 = h1;
        System.Console.WriteLine($"after move to h2={h2.Describe()}");
        Consume(h2);
        System.Console.WriteLine($"transfer complete");
    }

    public static MoveDemo __CreateDefault()
    {
        var inst = new MoveDemo();
        return inst;
    }
}
