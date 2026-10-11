using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public readonly struct Container
{
    public readonly int Code;
    public readonly string Label;

    public Container()
    {
        this.Code = default(int);
        this.Label = "";
        this.Code = 99;
        this.Label = "constructed";
    }

    private Container(int p0, string p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Code = p0;
        this.Label = p1;
    }

    internal static Container __FromParts(int p0, string p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Container(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    private Container(Mist.Emission.CSharp.__DefaultInit marker)
    {
        this.Code = 42;
        this.Label = "defaulted";
    }

    public static Container __CreateDefault()
    {
        return new Container(default(Mist.Emission.CSharp.__DefaultInit));
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Container c1 = new Snippet.Container();
        Snippet.Container c2 = Snippet.Container.__CreateDefault();
        System.Console.WriteLine(System.FormattableString.Invariant($"new Container() = Code={c1.Code}, Label={c1.Label}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"default(Container) = Code={c2.Code}, Label={c2.Label}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
