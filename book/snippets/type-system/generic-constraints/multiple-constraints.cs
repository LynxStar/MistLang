using System;

namespace Snippet;

class PrintableCreator<T>
    where T : class, new()
{
    public T CreateNew()
    {
        return new T();
    }
}
