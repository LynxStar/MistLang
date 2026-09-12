using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] numbers = new int[] { 10, 20, 30, 40, 50 };
        string label = numbers switch { [] => "empty", [var _] => "single", [var _, var _] => "pair", var _ => "many" };
        System.Console.WriteLine($"label={label}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
