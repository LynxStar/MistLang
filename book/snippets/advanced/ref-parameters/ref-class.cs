using System;

namespace Snippet;

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
}
public class Program
{
    private static void ReplaceContainer(ref Container c)
    {
        c = new Container(999);
    }

    public static void Main()
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var box = new Container(42);
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
