using System;

namespace Snippet;

public interface IStrictProcessor
{
    string Process(int x);
}

class WeakerProcessor : Snippet.IStrictProcessor
{
    public string Process(int x)
    {
        return $"WeakerProcessor.Process({x}) accepted (weaker constraint >= 0)";
    }

    public WeakerProcessor()
    {
    }

    public static WeakerProcessor __CreateDefault()
    {
        return new WeakerProcessor();
    }
}

class LenientProcessor : Snippet.IStrictProcessor
{
    public string Process(int x)
    {
        return $"LenientProcessor.Process({x}) accepted (no constraint)";
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
        Console.WriteLine(weaker.Process(50));
        Snippet.IStrictProcessor lenient = new Snippet.LenientProcessor();
        Console.WriteLine(lenient.Process(50));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
