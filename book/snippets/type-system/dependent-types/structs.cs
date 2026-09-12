using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct FixedSpan<TLength>
    where TLength : IConstInt
{
    private FixedSpan(Mist.Emission.CSharp.__StructInit marker)
    {
    }

    internal static FixedSpan<TLength> __FromParts(Mist.Emission.CSharp.__StructInit marker)
    {
        return new FixedSpan<TLength>(default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly int GetLength()
    {
        return TLength.Value;
    }

    public readonly string Describe()
    {
        return $"FixedSpan<{TLength.Value}>";
    }

    public FixedSpan()
    {
    }

    public static FixedSpan<TLength> __CreateDefault()
    {
        return new FixedSpan<TLength>();
    }
}
