using Standard;

namespace Snippet;

public interface IStrictProcessor
{
    string Process(int x);
}

internal class WeakerProcessor : Snippet.IStrictProcessor
{
    public string Process(int x)
    {
        return System.FormattableString.Invariant($"WeakerProcessor.Process({x}) accepted (weaker constraint >= 0)");
    }

    public WeakerProcessor()
    {
    }

    public static WeakerProcessor __CreateDefault()
    {
        return new WeakerProcessor();
    }
}

internal class LenientProcessor : Snippet.IStrictProcessor
{
    public string Process(int x)
    {
        return System.FormattableString.Invariant($"LenientProcessor.Process({x}) accepted (no constraint)");
    }

    public LenientProcessor()
    {
    }

    public static LenientProcessor __CreateDefault()
    {
        return new LenientProcessor();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.IStrictProcessor weaker = new Snippet.WeakerProcessor();
        System.Console.WriteLine(weaker.Process(50));
        Snippet.IStrictProcessor lenient = new Snippet.LenientProcessor();
        System.Console.WriteLine(lenient.Process(50));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
