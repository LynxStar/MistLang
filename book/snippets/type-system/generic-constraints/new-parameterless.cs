using System;

namespace Snippet;

public class Creator<T>
    where T : new()
{
    public T Create()
    {
        return new T();
    }
}
