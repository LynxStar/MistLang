using System;

namespace Snippet;

[DependentType("Length", typeof(int))]
public struct FixedSpan<TLength>
    where TLength : IConstInt
{
    public static int Length => TLength.Value;

    public readonly int GetLength()
    {
        return TLength.Value;
    }

    public readonly string Describe()
    {
        return $"FixedSpan<{TLength.Value}>";
    }
}
