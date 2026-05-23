using Standard;
using System;

namespace CastOperators;

public class Program
{
    public static void Main(string[] args)
    {
        var body = new Celsius(37.0);
        var bodyK = ((Kelvin)body);
        System.Console.WriteLine(bodyK.Describe());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
