using System;

namespace DependentTypes;

interface ISized<TN>
    where TN : IConstInt
{
    int GetCapacity();
}
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
