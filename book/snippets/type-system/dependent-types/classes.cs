using Standard;
using System;

namespace DependentTypes;

[DependentType("Size", typeof(int))]
public class TypedBuffer<T, TSize>
    where TSize : IConstInt
{
    public static int Size => TSize.Value;

    public T Value;

    public TypedBuffer(T value)
    {
        this.Value = value;
    }

    public string Describe()
    {
        return $"TypedBuffer<{TSize.Value}>(value={this.Value})";
    }
}
