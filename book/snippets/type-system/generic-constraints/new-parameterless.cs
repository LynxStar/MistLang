using System;

namespace Snippet;

internal class Creator<T>
    where T : new()
{
    public T Create()
    {
        return new T();
    }
}
