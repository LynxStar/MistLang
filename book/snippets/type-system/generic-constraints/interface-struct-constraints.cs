interface IRepository<T>
    where T : class, new()
{
    T GetById(int id);
    void Save(T item);
}

struct Holder<T>
    where T : IPrintable
{
    public T Value;

    public Holder(T value)
    {
        Value = value;
    }
}
