using Standard;

namespace Snippet;

public class KeyValueStore<K, V>
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    private K key;
    [System.Diagnostics.CodeAnalysis.AllowNull]
    private V value;

    public KeyValueStore(K key, V value)
    {
        this.key = key;
        this.value = value;
    }

    public K GetKey()
    {
        return this.key;
    }

    public V GetValue()
    {
        return this.value;
    }

    public string Describe()
    {
        return $"{MistInvariant.Text(this.key)} => {MistInvariant.Text(this.value)}";
    }
}
