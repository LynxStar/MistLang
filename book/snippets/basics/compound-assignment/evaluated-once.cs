using Standard;

namespace Snippet;

public class Picker
{
    public int Calls;
    public string Log = "";

    public int Next()
    {
        this.Calls++;
        return this.Calls - 1;
    }

    public int Logged(string step, int result)
    {
        this.Log += step + " ";
        return result;
    }

    public Picker()
    {
        this.Calls = default(int);
    }

    public static Picker __CreateDefault()
    {
        return new Picker();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        System.Collections.Generic.List<int> items = new System.Collections.Generic.List<int>();
        items.Add(10);
        items.Add(20);
        items.Add(30);
        Snippet.Picker picker = new Snippet.Picker();
        items[picker.Next()] += 5;
        items[picker.Next()] *= 3;
        System.Console.WriteLine(System.FormattableString.Invariant($"{items[0]}, {items[1]}, {items[2]}; Next ran {picker.Calls} times"));
        Snippet.Picker order = new Snippet.Picker();
        items[order.Logged("index", 2)] += order.Logged("value", 100);
        System.Console.WriteLine(System.FormattableString.Invariant($"{order.Log}-> items[2] is {items[2]}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
