using System;

namespace Snippet;

interface IStrictProcessor
{
    string Process(int x);
}
class WeakerProcessor : IStrictProcessor
{
    public string Process(int x)
    {
        return $"WeakerProcessor.Process({x}) accepted (weaker constraint >= 0)";
    }

    public static WeakerProcessor __CreateDefault()
    {
        var inst = new WeakerProcessor();
        return inst;
    }
}
class LenientProcessor : IStrictProcessor
{
    public string Process(int x)
    {
        return $"LenientProcessor.Process({x}) accepted (no constraint)";
    }

    public static LenientProcessor __CreateDefault()
    {
        var inst = new LenientProcessor();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        IStrictProcessor weaker = new WeakerProcessor();
        Console.WriteLine(weaker.Process(50));
        IStrictProcessor lenient = new LenientProcessor();
        Console.WriteLine(lenient.Process(50));
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
