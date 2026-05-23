using System;

namespace Snippet;

public readonly struct Container
{
    public readonly int Code;

    public readonly string Label;

    public Container()
    {
        this.Code = 99;
        this.Label = "constructed";
    }


    private Container(bool __defaultSentinel)
    {
        this.Code = 42;
        this.Label = "defaulted";
    }

    public static Container __CreateDefault()
    {
        var inst = new Container(true);
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var c1 = new Container();
        Container c2 = Container.__CreateDefault();
        Console.WriteLine($"new Container() = Code={c1.Code}, Label={c1.Label}");
        Console.WriteLine($"default(Container) = Code={c2.Code}, Label={c2.Label}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
