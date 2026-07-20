using System;

namespace Snippet;

public class Bundle<T1, T2, T3>
{
    public System.ValueTuple<T1, T2, T3> Item;
    public T1 Item1 { get => Item.Item1; set => Item.Item1 = value; }
    public T2 Item2 { get => Item.Item2; set => Item.Item2 = value; }
    public T3 Item3 { get => Item.Item3; set => Item.Item3 = value; }

    public Bundle(System.ValueTuple<T1, T2, T3> values)
    {
        Item = values;
    }

    public TResult Map<TResult>(System.Func<T1, T2, T3, TResult> f)
    {
        return f(Item.Item1, Item.Item2, Item.Item3);
    }
}

public class Program
{
    public static void Main(string[] cmdArgs)
    {
        var bundle = new Bundle<int, string, bool>((42, "hi", true));
        System.Console.WriteLine($"agg={bundle.Item.Item1}:{bundle.Item.Item2}:{bundle.Item.Item3}");
        System.Console.WriteLine($"item1={bundle.Item1}");

        Func<int, string, bool, string> fmt = (a, b, c) => $"{a}-{b}-{c}";
        var mapped = bundle.Map(fmt);
        System.Console.WriteLine($"map={mapped}");
    }
}
