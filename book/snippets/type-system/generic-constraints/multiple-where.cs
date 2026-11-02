using Standard;

namespace Snippet;

internal class Pair<K, V>
    where K : Snippet.IOrderable
    where V : new()
{
    private readonly System.Func<V> __construct_V_0;
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public K Key;
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public V Value;

    public Pair(K key) : this(() => new V(), key)
    {
    }

    public Pair(System.Func<V> __construct_V_0, K key)
    {
        this.__construct_V_0 = __construct_V_0;
        this.Key = key;
        this.Value = __construct_V_0();
    }
}
