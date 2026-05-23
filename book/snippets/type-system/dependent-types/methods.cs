using System;

namespace Snippet;

public class DependentMethods
{
    public static void Repeat<TN>(string text)
        where TN : IConstInt
    {
        Console.WriteLine($"  Repeat<{TN.Value}>: {text}");
    }

    public static string Process<TN>(FixedBuffer<N> buf)
        where TN : IConstInt
    {
        return $"Processed buffer of size {buf.GetSize()}";
    }

    public static DependentMethods __CreateDefault()
    {
        var inst = new DependentMethods();
        return inst;
    }
}
