using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Pair<A, B>
{
    public A First;
    public B Second;

    public Pair(A first, B second)
    {
        this.First = first;
        this.Second = second;
    }

    private Pair(A p0, B p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.First = p0;
        this.Second = p1;
    }

    internal static Pair<A, B> __FromParts(A p0, B p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Pair<A, B>(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly string Describe()
    {
        return $"({MistInvariant.Text(First)}, {MistInvariant.Text(Second)})";
    }
}
