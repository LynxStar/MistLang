using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        ((int, int), string) nested = ((1, 2), "label");
        if (nested is ((> 0, > 0), "label"))
        {
            System.Console.WriteLine("nested tuple: both positive with label");
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
