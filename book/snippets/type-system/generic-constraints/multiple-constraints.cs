interface IPrintable
{
    string Print();
}

class PrintableCreator<T>
    where T : class, IPrintable, new()
{
    public T CreateNew()
    {
        return new T();
    }
}
