using System;

namespace CastOperators;

public class Program
{
    public static void Main(string[] args)
    {
        var body = new Celsius(37.0);
        var bodyK = ((Kelvin)body);
        Console.WriteLine(bodyK.Describe());
    }
}
