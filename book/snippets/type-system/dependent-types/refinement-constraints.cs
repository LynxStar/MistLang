using System;

namespace Snippet;

public class SizedCollection<TCount>
    where TCount : IConstInt
{
    public int GetCount()
    {
        return TCount.Value;
    }

    public string Describe()
    {
        return $"SizedCollection<{TCount.Value}>";
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
    where TCapacity : IConstInt
{
    private FixedSlot(Mist.Emission.CSharp.__StructInit marker)
    {
    }

    internal static FixedSlot<TCapacity> __FromParts(Mist.Emission.CSharp.__StructInit marker)
    {
        return new FixedSlot<TCapacity>(default(Mist.Emission.CSharp.__StructInit));
    }

    public int GetCapacity()
    {
        return TCapacity.Value;
    }

    public FixedSlot()
    {
    }

    public static FixedSlot<TCapacity> __CreateDefault()
    {
        return new FixedSlot<TCapacity>();
    }
}
