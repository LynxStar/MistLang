using System;

namespace Snippet;

class Program
{
    public static void Main(string[] args)
    {
        var city = "Portland";
        var population = 650000;
        var info = new { city, population };
        System.Console.WriteLine($"city={info.city} population={info.population}");
        var lang = new { Name = "Mist", Version = 1 };
        var projected = new { lang.Name };
        System.Console.WriteLine($"projected.Name={projected.Name}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
