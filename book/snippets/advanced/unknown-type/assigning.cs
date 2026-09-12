using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        object u = 42;
        System.Console.WriteLine(u);
        u = "hello";
        System.Console.WriteLine(u);
        u = true;
        System.Console.WriteLine(u);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
