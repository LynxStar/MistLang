using System;

namespace DependentTypes;

[DependentType("Length", typeof(int))]
public struct FixedSpan<TLength>
    where TLength : IConstInt
{
    public static int Length => TLength.Value;

    public int GetLength() { return Length; }
    public string Describe() { return $"FixedSpan<{Length}>"; }
}
