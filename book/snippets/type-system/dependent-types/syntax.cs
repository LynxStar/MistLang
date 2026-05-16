using System;

namespace DependentTypes;

[DependentType("Size", typeof(int))]
public class FixedBuffer<TSize>
    where TSize : IConstInt
{
    public static int Size => TSize.Value;

    public int GetSize() { return Size; }
    public string Describe() { return $"FixedBuffer<{Size}>"; }
}
