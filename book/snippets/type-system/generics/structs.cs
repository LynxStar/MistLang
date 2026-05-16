using System;

namespace GenericTypes;

public struct Pair<A, B>
{
    public A First;

    public B Second;

    public Pair(A first, B second)
    {
        this.First = first;
        this.Second = second;
    }

    public string Describe()
    {
        return $"({First}, {Second})";
    }
}
