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
        Snippet.Pair<int> intPair = new Snippet.Pair<int>(1, 2);
        Snippet.Pair<string> strPair = new Snippet.Pair<string>("hello", "world");
        Console.WriteLine(intPair.Describe());
        Console.WriteLine(strPair.Describe());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
