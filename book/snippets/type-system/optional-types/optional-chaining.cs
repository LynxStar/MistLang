using Standard;

namespace Snippet;

public class Item
{
    public int value;

    public Item(int v)
    {
        this.value = default(int);
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
        Snippet.Item? present = new Snippet.Item(5);
        Snippet.Item? absent = null;
        int? result1 = present?.GetValue();
        int? result2 = absent?.GetValue();
        System.Console.WriteLine(System.FormattableString.Invariant($"present?.GetValue()={(object?)result1 ?? "Void"}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"absent?.GetValue()={(object?)result2 ?? "Void"}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
