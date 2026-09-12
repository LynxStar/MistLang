using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        string outer = "visible everywhere";
        {
            string inner = "only visible in this block";
            System.Console.WriteLine(inner);
            System.Console.WriteLine(outer);
        }
        System.Console.WriteLine(outer);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
