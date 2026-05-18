using System;

namespace RefParameters;

public class Container
{
    public int Value;

    public Container(int value)
    {
        this.Value = value;
    }

    public string Describe()
    {
        return $"Container({Value})";
    }

    protected Container()
    {
        this.Value = 0;
    }

    public static Container __CreateDefault()
    {
        var inst = new Container();
        return inst;
    }
}
public class Program
{
    private static void ReplaceContainer(ref Container c)
    {
        c = new Container(999);
    }

    public static void Main()
    {
        Container box = new Container(42);
        Console.WriteLine($"before: {box.Describe()}");
        ReplaceContainer(ref box);
        Console.WriteLine($"after: {box.Describe()}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
