using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Greeting greeting = new Snippet.Greeting("Hello beautiful world");
        int wc = Snippet.GreetingExtensions_declaring_0.WordCount(greeting);
        string upper = Snippet.GreetingExtensions_declaring_0.Uppercase(greeting);
        Snippet.Greeting repeated = Snippet.GreetingExtensions_withparams_0.Repeat(greeting, 2);
        System.Console.WriteLine(wc.ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(upper);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
