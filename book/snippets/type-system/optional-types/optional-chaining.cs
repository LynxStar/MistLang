using System;

namespace Snippet;

public class Item
{
    public int value;

    public Item(int v)
    {
        this.value = v;
    }

    public int GetValue()
    {
        return this.value;
    }

    protected Item()
    {
        this.value = 0;
    }

    public static Item __CreateDefault()
    {
        var inst = new Item();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        Item present = new Item(5);
        Item absent = null;
        int? result1 = present?.GetValue();
        int? result2 = absent?.GetValue();
        Console.WriteLine($"present?.GetValue()={result1}");
        Console.WriteLine($"absent?.GetValue()={result2}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
