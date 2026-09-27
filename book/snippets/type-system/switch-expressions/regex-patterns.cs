using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        string input = "hello";
        string category = input switch { var __e1 when System.Text.RegularExpressions.Regex.IsMatch(__e1, "^(?:" + "^\\d+$" + ")$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) => "number", var __e2 when System.Text.RegularExpressions.Regex.IsMatch(__e2, "^(?:" + "^[a-z]+$" + ")$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) => "lowercase", var __e3 when System.Text.RegularExpressions.Regex.IsMatch(__e3, "^(?:" + "^[A-Z]+$" + ")$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) => "uppercase", var _ => "other" };
        System.Console.WriteLine($"'{input}' is {category}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
