using Standard;

namespace Snippet;

public class Item
{
    public int value;

    public Item(int v)
    {
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
        Snippet.Item present = new Snippet.Item(5);
        Snippet.Item absent = null;
        int? result1 = present?.GetValue();
        int? result2 = absent?.GetValue();
        System.Console.WriteLine($"present?.GetValue()={(object?)result1 ?? "Void"}");
        System.Console.WriteLine($"absent?.GetValue()={(object?)result2 ?? "Void"}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
