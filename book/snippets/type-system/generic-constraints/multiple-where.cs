using Standard;

namespace Snippet;

internal class Pair<K, V>
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
