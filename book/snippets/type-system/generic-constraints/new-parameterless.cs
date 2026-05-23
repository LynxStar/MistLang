using System;

namespace Snippet;

class Creator<T>
    where T : new()
{
    public T Create()
    {
        return new T();
    }
}
