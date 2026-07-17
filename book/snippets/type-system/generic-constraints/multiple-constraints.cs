using System;

namespace Snippet;

public interface IPrintable
{
    string Print();
}
internal class PrintableCreator<T>
    where T : class, IPrintable, new()
{
    public T CreateNew()
    {
        return new T();
    }
}
