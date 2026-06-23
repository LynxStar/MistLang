using System;

namespace Snippet;

struct Holder<T>
{
    public T Value;

    public Holder(T value)
    {
        Value = value;
    }
}
