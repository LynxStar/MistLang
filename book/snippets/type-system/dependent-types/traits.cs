using System;

namespace Snippet;

[DependentType("N", typeof(int))]
public class SizedBox<TN>
    where TN : IConstInt
{
    public static int N => TN.Value;

    public int GetCapacity()
    {
        return TN.Value;
    }
}
