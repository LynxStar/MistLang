namespace TypeAliases;

public class Program
{
    private static void PrintUserId(int id)
    {
        Console.WriteLine(id);
    }

    public static void Main(string[] args)
    {
        int aliasedId = 100;
        int rawId = aliasedId;
        int plainInt = 7;
        Program.PrintUserId(plainInt);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
