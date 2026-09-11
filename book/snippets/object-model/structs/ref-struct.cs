using System;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public ref struct IntBuffer
{
    public int value;
    public int capacity;

    public IntBuffer(int value, int capacity)
    {
        this.value = value;
        this.capacity = capacity;
    }

    private IntBuffer(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.value = p0;
        this.capacity = p1;
    }

    internal static IntBuffer __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new IntBuffer(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly int Remaining()
    {
        return this.capacity - this.value;
    }
}
