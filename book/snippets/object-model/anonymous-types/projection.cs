using System;

namespace Snippet;

class Program
{
    public static void Main(string[] args)
    {
        string city = "Portland";
        int population = 650000;
        var info = new { city = city, population = population };
        Console.WriteLine($"city={info.city} population={info.population}");
        var lang = new { Name = "Mist", Version = 1 };
        var projected = new { Name = lang.Name };
        Console.WriteLine($"projected.Name={projected.Name}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
