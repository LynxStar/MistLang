using System;

namespace Snippet;

public class FixedBuffer<TSize>
    where TSize : IConstInt
{
    public int GetSize()
    {
        return TSize.Value;
    }

    public string Describe()
    {
        return $"FixedBuffer<{TSize.Value}>";
    }

    public FixedBuffer()
    {
    }

    public static FixedBuffer<TSize> __CreateDefault()
    {
        return new FixedBuffer<TSize>();
    }
}

public class DependentMethods
{
    public static void Repeat<TN>(string text)
        where TN : IConstInt
    {
        Console.WriteLine($"  Repeat<{TN.Value}>: {text}");
    }

    public static string Process<TN>(Snippet.FixedBuffer<TN> buf)
        where TN : IConstInt
    {
        return $"Processed buffer of size {buf.GetSize()}";
    }

    public DependentMethods()
    {
    }

    public static DependentMethods __CreateDefault()
    {
        return new DependentMethods();
    }
}
