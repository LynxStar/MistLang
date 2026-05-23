using Standard;
using System;

namespace AssociatedTypesDemo;

interface IStringable
{
    string Stringify();
}
interface IProducer<TOutput>
    where TOutput : IStringable
{
    TOutput CreateOutput(string msg);
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine("IProducer declares Output : IStringable");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
