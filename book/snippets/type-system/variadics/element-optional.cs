using Standard;

namespace Snippet;

public class Pick__P2__Standard_String__Standard_Int32
{
    public int? Second;

    public string Show()
    {
        {
            if (this.Second is int kept)
            {
                return System.FormattableString.Invariant($"kept {kept}");
            }
        }
        return "none";
    }

    public Pick__P2__Standard_String__Standard_Int32()
    {
        this.Second = null;
    }

    public static Pick__P2__Standard_String__Standard_Int32 __CreateDefault()
    {
        return new Pick__P2__Standard_String__Standard_Int32();
    }
}

public class Pick__P2__Standard_Int32__Standard_String
{
    public string Second;

    public string Show()
    {
        {
            if (this.Second is string kept)
            {
                return $"kept {kept}";
            }
        }
        return "none";
    }

    public Pick__P2__Standard_Int32__Standard_String()
    {
        this.Second = null;
    }

    public static Pick__P2__Standard_Int32__Standard_String __CreateDefault()
    {
        return new Pick__P2__Standard_Int32__Standard_String();
    }
}

public class Program
{
    public static void Main(string[] cmdArgs)
    {
        Snippet.Pick__P2__Standard_String__Standard_Int32 numbers = new Snippet.Pick__P2__Standard_String__Standard_Int32();
        Snippet.Pick__P2__Standard_Int32__Standard_String words = new Snippet.Pick__P2__Standard_Int32__Standard_String();
        words.Second = "b";
        System.Console.WriteLine($"{numbers.Show()} {words.Show()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
