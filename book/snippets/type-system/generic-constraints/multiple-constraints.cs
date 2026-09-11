using System;

namespace Snippet;

internal interface IPrintable
{
    string Print();
}

internal class PrintableCreator<T>
    where T : class, Snippet.IPrintable, new()
{
    public T CreateNew()
    {
        return new T();
    }

    public PrintableCreator()
    {
    }

    public static PrintableCreator<T> __CreateDefault()
    {
        return new PrintableCreator<T>();
    }
}
