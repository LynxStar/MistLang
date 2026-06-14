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
}
public class Program
{
    public static void Main(string[] args)
    {
        Item present = new Item(5);
        Item absent = null;
        int? result1 = present?.GetValue();
        int? result2 = absent?.GetValue();
        Console.WriteLine($"present?.GetValue()={(object?)result1 ?? "Void"}");
        Console.WriteLine($"absent?.GetValue()={(object?)result2 ?? "Void"}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
