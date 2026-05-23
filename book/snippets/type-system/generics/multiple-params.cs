using Standard;
using System;

namespace GenericTypes;

public class KeyValueStore<K, V>
{
    private K key;

    private V value;

    public KeyValueStore(K key, V value)
    {
        this.key = key;
        this.value = value;
    }

    public K GetKey()
    {
        return key;
    }

    public V GetValue()
    {
        return value;
    }

    public string Describe()
    {
        return $"{key} => {value}";
    }
}
