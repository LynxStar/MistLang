using Standard;
using System;

namespace AssociatedTypesDemo;

interface Container<TElement>
{
    TElement Get(int index);
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine("Container declares unconstrained Element");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
