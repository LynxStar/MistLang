using System;

namespace Snippet;

public class Utilities
{
    public static T Identity<T>(T value)
    {
        return value;
    }

    public static void PrintItem<T>(T item)
    {
        Console.WriteLine($"Item: {item}");
    }

    public static Utilities __CreateDefault()
    {
        var inst = new Utilities();
        return inst;
    }
}
