using System;

namespace Snippet;

interface IOrderable
{
    int GetOrder();
}

class Pair<K, V>
    where K : Snippet.IOrderable
    where V : new()
{
    public K Key;
    public V Value;

    public Pair(K key)
    {
        Key = key;
        Value = new V();
    }
}
