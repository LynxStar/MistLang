using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        object u = 42;
        System.Console.WriteLine(MistInvariant.Text(u));
        u = "hello";
        System.Console.WriteLine(MistInvariant.Text(u));
        u = true;
        System.Console.WriteLine(MistInvariant.Text(u));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
