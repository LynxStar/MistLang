using System;

namespace Snippet;

public ref struct IntBuffer
{
    public int value;

    public int capacity;

    public IntBuffer(int value, int capacity)
    {
        this.value = value;
        this.capacity = capacity;
    }

    public readonly int Remaining()
    {
        return this.capacity - this.value;
    }

    public static IntBuffer __CreateDefault()
    {
        var inst = new IntBuffer();
        inst.value = 0;
        inst.capacity = 0;
        return inst;
    }
}
