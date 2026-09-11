using System;

namespace Snippet;

class Creator<T>
    where T : new()
{
    public T Create()
    {
        return new T();
    }

    public Creator()
    {
    }

    public static Creator<T> __CreateDefault()
    {
        return new Creator<T>();
    }
}
