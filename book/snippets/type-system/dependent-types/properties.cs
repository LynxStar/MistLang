using Standard;

namespace Snippet;

public class FixedBuffer<TSize>
    where TSize : IConstIntegral
{
    public static int Size => ((int)TSize.Value);

    public FixedBuffer()
    {
    }

    public static FixedBuffer<TSize> __CreateDefault()
    {
        return new FixedBuffer<TSize>();
    }
}

public class SmallBuffer : Snippet.FixedBuffer<Const_10>
{
    public SmallBuffer()
    {
    }

    public new static SmallBuffer __CreateDefault()
    {
        return new SmallBuffer();
    }
}

public class Program
{
    public static void Main()
    {
        System.Console.WriteLine(Snippet.FixedBuffer<Const_5>.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(Snippet.FixedBuffer<Const_5>.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(Snippet.FixedBuffer<Const_10>.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(((int)6).ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(((int)7).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
