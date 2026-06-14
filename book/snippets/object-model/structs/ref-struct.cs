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
}
