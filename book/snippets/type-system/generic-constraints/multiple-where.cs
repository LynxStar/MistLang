using System;

namespace Snippet;

internal class Pair<K, V>
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
