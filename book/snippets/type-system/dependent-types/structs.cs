using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct FixedSpan<TLength>
    where TLength : IConstIntegral
{
    public static int Length => ((int)TLength.Value);

    private FixedSpan(Mist.Emission.CSharp.__StructInit marker)
    {
    }

    internal static FixedSpan<TLength> __FromParts(Mist.Emission.CSharp.__StructInit marker)
    {
        return new FixedSpan<TLength>(default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly int GetLength()
    {
        return ((int)TLength.Value);
    }

    public readonly string Describe()
    {
        return System.FormattableString.Invariant($"FixedSpan<{((int)TLength.Value)}>");
    }

    public FixedSpan()
    {
    }

    public static FixedSpan<TLength> __CreateDefault()
    {
        return new FixedSpan<TLength>();
    }
}
