using System;

namespace Snippet;

public interface IRepository<T>
    where T : class, new()
{
    T GetById(int id);
    void Save(T item);
}
internal struct Holder<T>
{
    public T Value;

    public Holder(T value)
    {
        Value = value;
    }
}
