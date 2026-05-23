using Standard;
using System;

namespace DependentTypes;

[DependentType("Size", typeof(int))]
public class FixedBuffer<TSize>
    where TSize : IConstInt
{
    public static int Size => TSize.Value;

    public int GetSize()
    {
        return TSize.Value;
    }

    public string Describe()
    {
        return $"FixedBuffer<{TSize.Value}>";
    }
}
