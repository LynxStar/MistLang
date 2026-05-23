using Standard;
using System;

namespace GenericTypes;

public class MultiParamDemo
{
    public static void Run()
    {
        var store = new KeyValueStore<string, int>("age", 30);
        System.Console.WriteLine(store.Describe());
        var mixed = new KeyValueStore<int, bool>(1, true);
        System.Console.WriteLine(mixed.Describe());
    }

    public static MultiParamDemo __CreateDefault()
    {
        var inst = new MultiParamDemo();
        return inst;
    }
}
