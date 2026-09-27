using Standard;

namespace Snippet;

public class DependentMethods
{
    public static void Repeat<TN>(string text)
        where TN : IConstInt
    {
        System.Console.WriteLine($"  Repeat<{MistInvariant.Text(TN.Value)}>: {text}");
    }

    public static string Process<TN>(Snippet.FixedBuffer<TN> buf)
        where TN : IConstInt
    {
        return System.FormattableString.Invariant($"Processed buffer of size {buf.GetSize()}");
    }

    public DependentMethods()
    {
    }

    public static DependentMethods __CreateDefault()
    {
        return new DependentMethods();
    }
}
