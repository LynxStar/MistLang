using System;

namespace Snippet;

public struct Locale
{
    public string Language;

    public Locale(string language)
    {
        this.Language = language;
    }

    public static Locale __CreateDefault()
    {
        var inst = new Locale();
        inst.Language = "";
        return inst;
    }
}
public class Consumers
{
    public static string GetLocale()
    {
        var loc = (Locale)MistContext.Peek<Locale>();
        return loc.Language;
    }

    public static Consumers __CreateDefault()
    {
        var inst = new Consumers();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        MistContext.Push<Locale>(new Locale("en-US"));
        try
        {
            var result = Consumers.GetLocale();
            Console.WriteLine(result);
        }
        finally
        {
            MistContext.Pop<Locale>();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
