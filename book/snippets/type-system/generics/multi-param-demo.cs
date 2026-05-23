using System;

namespace Snippet;

public class MultiParamDemo
{
    public static void Run()
    {
        var store = new KeyValueStore<string, int>("age", 30);
        Console.WriteLine(store.Describe());
        var mixed = new KeyValueStore<int, bool>(1, true);
        Console.WriteLine(mixed.Describe());
    }

    public static MultiParamDemo __CreateDefault()
    {
        var inst = new MultiParamDemo();
        return inst;
    }
}
