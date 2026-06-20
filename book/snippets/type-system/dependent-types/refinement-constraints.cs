using System;

namespace Snippet;

[DependentType("Count", typeof(int))]
public class SizedCollection<Count>
    where TCount : IConstInt
{
    public static int Count => TCount.Value;

    public int GetCount()
    {
        return TCount.Value;
    }

    public string Describe()
    {
        return $"SizedCollection<{TCount.Value}>";
    }
}
[DependentType("Capacity", typeof(int))]
public struct FixedSlot<Capacity>
    where TCapacity : IConstInt
{
    public static int Capacity => TCapacity.Value;

    public readonly int GetCapacity()
    {
        return TCapacity.Value;
    }
}
