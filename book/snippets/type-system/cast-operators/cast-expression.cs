using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Celsius body = new Snippet.Celsius(37.0);
        Snippet.Kelvin bodyK = ((Snippet.Kelvin)(body));
        System.Console.WriteLine(bodyK.Describe());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
