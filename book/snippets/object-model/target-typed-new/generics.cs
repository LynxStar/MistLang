using Standard;

namespace Snippet;

internal class Pair<T>
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public T First;
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public T Second;

    public Pair(T first, T second)
    {
        this.First = first;
        this.Second = second;
    }

    public string Describe()
    {
        return $"Pair({MistInvariant.Text(this.First)}, {MistInvariant.Text(this.Second)})";
    }
}

internal class Program
{
    public static void Main(string[] args)
    {
        Snippet.Pair<int> intPair = new Snippet.Pair<int>(1, 2);
        Snippet.Pair<string> strPair = new Snippet.Pair<string>("hello", "world");
        System.Console.WriteLine(intPair.Describe());
        System.Console.WriteLine(strPair.Describe());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
