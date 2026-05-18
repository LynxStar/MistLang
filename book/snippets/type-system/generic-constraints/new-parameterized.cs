namespace Snippet;

class IntFactory<T>
{
    private readonly Func<int, T> _factoryT_int;

    public IntFactory(Func<int, T> factoryT_int)
    {
        _factoryT_int = factoryT_int;
    }

    public T Create(int value)
    {
        return _factoryT_int(value);
    }
}
