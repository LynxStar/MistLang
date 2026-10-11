using Standard;

namespace StampableInference;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Box<T>
{
    public T Item;

    public Box(T item)
    {
        this.Item = default(Box<T>).Item;
        Item = item;
    }

    private Box(T p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Item = p0;
    }

    internal static Box<T> __FromParts(T p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Box<T>(p0, default(Mist.Emission.CSharp.__StructInit));
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        StampableInference.Box<int>[] numbers = new StampableInference.Box<int>[2];
        System.Console.WriteLine(System.FormattableString.Invariant($"numbers[0].Item={numbers[0].Item}"));
        StampableInference.Box<string> text = new StampableInference.Box<string>("ready");
        System.Console.WriteLine($"text.Item={text.Item}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
