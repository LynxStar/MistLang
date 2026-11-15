using Standard;

namespace Snippet;

public class Program
{
    public static void Board(string code)
    {
        System.Console.WriteLine($"Boarding for {code}");
    }

    public static void Route(string text, string known)
    {
        if (text == "LHR")
        {
            Board(text);
        }
        if (text == known)
        {
            Board(text);
        }
        string code = "DEF";
        if ((text is var __e1309 && System.Text.RegularExpressions.Regex.IsMatch(__e1309, "\\A(?:" + "^[A-Z]{3}$" + "(?#\n(?#))\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
        {
            code = text;
        }
        Board(code);
    }

    public static void Main(string[] args)
    {
        Route("LHR", "LHR");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
