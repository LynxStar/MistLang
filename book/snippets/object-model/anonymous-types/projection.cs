using Standard;

namespace Snippet;

internal class Program
{
    public static void Main(string[] args)
    {
        string city = "Portland";
        int population = 650000;
        var info = new { city = city, population = population };
        System.Console.WriteLine(System.FormattableString.Invariant($"city={info.city} population={info.population}"));
        var lang = new { Name = "Mist", Version = 1 };
        var projected = new { Name = lang.Name };
        System.Console.WriteLine($"projected.Name={projected.Name}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
