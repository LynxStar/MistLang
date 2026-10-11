using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Celsius
{
    public double Degrees;

    public Celsius(double degrees)
    {
        this.Degrees = default(double);
        Degrees = degrees;
    }

    private Celsius(double p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Degrees = p0;
    }

    internal static Celsius __FromParts(double p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Celsius(p0, default(Mist.Emission.CSharp.__StructInit));
    }

    public static explicit operator Snippet.Fahrenheit(Snippet.Celsius c)
    {
        return new Snippet.Fahrenheit(c.Degrees * 9.0 / 5.0 + 32.0);
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Fahrenheit
{
    public double Degrees;

    public Fahrenheit(double degrees)
    {
        this.Degrees = default(double);
        Degrees = degrees;
    }

    private Fahrenheit(double p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Degrees = p0;
    }

    internal static Fahrenheit __FromParts(double p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Fahrenheit(p0, default(Mist.Emission.CSharp.__StructInit));
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Celsius boiling = new Snippet.Celsius(100.0);
        Snippet.Fahrenheit f = ((Snippet.Fahrenheit)(boiling));
        System.Console.WriteLine(System.FormattableString.Invariant($"{f.Degrees} F"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
