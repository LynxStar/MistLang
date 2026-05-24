using System;

namespace Snippet;

class MultiFactory<T>
    where T : new()
{
    private readonly Func<int, T> _factoryT_int;
    private readonly Func<bool, T> _factoryT_bool;

    public MultiFactory(Func<int, T> factoryT_int, Func<bool, T> factoryT_bool)
    {
        _factoryT_int = factoryT_int;
        _factoryT_bool = factoryT_bool;
    }

    public T CreateDefault()
    {
        return new T();
    }

    public T CreateFromInt(int value)
    {
        return _factoryT_int(value);
    }

    public T CreateFromBool(bool flag)
    {
        return _factoryT_bool(flag);
    }
}
