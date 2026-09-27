using Standard;

namespace Snippet;

public class Program
{
    private static void PrintUserId(int id)
    {
        System.Console.WriteLine(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static void Main(string[] args)
    {
        int aliasedId = 100;
        int rawId = aliasedId;
        int plainInt = 7;
        Snippet.Program.PrintUserId(plainInt);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
