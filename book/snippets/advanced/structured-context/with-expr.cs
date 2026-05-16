using System;

namespace ContextDemo;

public struct Locale
{
    public string Language;

    public Locale(string language)
    {
        this.Language = language;
    }
}

public class Consumers
{
    public static string GetLocale()
    {
        var loc = (Locale)MistContext.Peek<Locale>();
        return loc.Language;
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
}
