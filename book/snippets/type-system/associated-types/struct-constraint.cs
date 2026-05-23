using Standard;
using System;

namespace AssociatedTypesDemo;

interface IIterator<TCursor>
    where TCursor : struct
{
    TCursor CreateCursor(int pos);
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine("IIterator declares Cursor : struct");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
