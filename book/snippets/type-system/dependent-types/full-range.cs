using Standard;

namespace Snippet;

public class Mask<TBits>
    where TBits : IConstIntegral
{
    public static ulong Bits => ((ulong)TBits.Value);

    public Mask()
    {
    }

    public static Mask<TBits> __CreateDefault()
    {
        return new Mask<TBits>();
    }
}

public class Program
{
    public static void Main()
    {
        System.Console.WriteLine(Snippet.Mask<Const_18446744073709551615>.Bits.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
