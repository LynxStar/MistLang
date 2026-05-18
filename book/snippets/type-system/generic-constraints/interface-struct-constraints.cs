namespace Snippet;

interface IRepository<T>
    where T : class, new()
{
    T GetById(int id);
    void Save(T item);
}
struct Holder<T>
{
    public T Value;

    public Holder(T value)
    {
        Value = value;
    }
}
