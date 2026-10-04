using Standard;

namespace Snippet;

public class SizedCollection<TCount>
    where TCount : IConstIntegral
{
    public static int Count => ((int)TCount.Value);

    public int GetCount()
    {
        return ((int)TCount.Value);
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"SizedCollection<{((int)TCount.Value)}>");
    }

    public SizedCollection()
    {
    }

    public static SizedCollection<TCount> __CreateDefault()
    {
        return new SizedCollection<TCount>();
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct FixedSlot<TCapacity>
    where TCapacity : IConstIntegral
{
    public static int Capacity => ((int)TCapacity.Value);

    private FixedSlot(Mist.Emission.CSharp.__StructInit marker)
    {
    }

    internal static FixedSlot<TCapacity> __FromParts(Mist.Emission.CSharp.__StructInit marker)
    {
        return new FixedSlot<TCapacity>(default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly int GetCapacity()
    {
        return ((int)TCapacity.Value);
    }

    public FixedSlot()
    {
    }

    public static FixedSlot<TCapacity> __CreateDefault()
    {
        return new FixedSlot<TCapacity>();
    }
}
