using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        string input = "hello";
        string category = input switch { var __e1309 when System.Text.RegularExpressions.Regex.IsMatch(__e1309, "\\A(?:" + "^\\d+$" + "(?#\n(?#))\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant) => "number", var __e1310 when System.Text.RegularExpressions.Regex.IsMatch(__e1310, "\\A(?:" + "^[a-z]+$" + "(?#\n(?#))\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant) => "lowercase", var __e1311 when System.Text.RegularExpressions.Regex.IsMatch(__e1311, "\\A(?:" + "^[A-Z]+$" + "(?#\n(?#))\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant) => "uppercase", var _ => "other" };
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
