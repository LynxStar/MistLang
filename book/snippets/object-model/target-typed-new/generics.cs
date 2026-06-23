using System;

namespace Snippet;

class Pair<T>
{
    public T First;

    public T Second;

    public Pair(T first, T second)
    {
        First = first;
        Second = second;
    }

    public string Describe()
    {
        return $"Pair({First}, {Second})";
    }
}
class Program
{
    public static void Main(string[] args)
    {
        Pair<int> intPair = new Pair<int>(1, 2);
        Pair<string> strPair = new Pair<string>("hello", "world");
        Console.WriteLine(intPair.Describe());
        Console.WriteLine(strPair.Describe());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
