using Standard;

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
        Snippet.Item[] items = MistArray.Generate<Snippet.Item>(5, (int i) => new Snippet.Item(System.FormattableString.Invariant($"item-{i}")));
        System.Console.WriteLine(items[0].Name);
        System.Console.WriteLine(items[4].Name);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
