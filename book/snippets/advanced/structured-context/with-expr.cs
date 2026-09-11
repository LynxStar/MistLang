using System;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Locale
{
    public string Language;

    public Locale(string language)
    {
        this.Language = language;
    }

    private Locale(string p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Language = p0;
    }

    internal static Locale __FromParts(string p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Locale(p0, default(Mist.Emission.CSharp.__StructInit));
    }
}

public class Consumers
{
    public static string GetLocale()
    {
        var loc = (Snippet.Locale)MistContext.Peek<Snippet.Locale>();
        return loc.Language;
    }

    public Consumers()
    {
    }

    public static Consumers __CreateDefault()
    {
        return new Consumers();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        {
            var __e1 = new Snippet.Locale("en-US");
            MistContext.Push<Snippet.Locale>(__e1);
            try
            {
                string result = Snippet.Consumers.GetLocale();
                Console.WriteLine(result);
            }
            finally
            {
                MistContext.Pop<Snippet.Locale>();
            }
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
