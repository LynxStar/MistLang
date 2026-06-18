using System;

namespace Snippet;

public class Item
{
    public string Name;

    public Item(string name)
    {
        this.Name = name;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Func<int, Item> __fn_0 = i => new Item($"item-{i}");
        var __arr_0 = new Item[5];
        for (int __i_0 = 0; __i_0 < 5; __i_0++) __arr_0[__i_0] = __fn_0(__i_0);
        var items = __arr_0;
        System.Console.WriteLine(items[0].Name);
        System.Console.WriteLine(items[4].Name);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
