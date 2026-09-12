using Standard;

namespace Snippet;

internal class Program
{
    public static void Main(string[] args)
    {
        var lang = new { Name = "Mist", Version = 1 };
        System.Console.WriteLine($"Name={lang.Name} Version={lang.Version}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
