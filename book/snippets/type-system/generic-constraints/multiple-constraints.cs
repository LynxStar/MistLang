using System;

namespace Snippet;

public interface IPrintable
{
    string Print();
}
public class PrintableCreator<T>
    where T : class, IPrintable, new()
{
    public T CreateNew()
    {
        return new T();
    }
}
