using System;

namespace DependentTypes;

public class DependentMethods
{
    public static void Repeat<TN>(string text)
        where TN : IConstInt
    {
        Console.WriteLine($"  Repeat<{TN.Value}>: {text}");
    }

    public static string Process<TN>(FixedBuffer<TN> buf)
        where TN : IConstInt
    {
        return $"Processed buffer of size {buf.GetSize()}";
    }
}
