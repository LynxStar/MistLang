using System.Text.RegularExpressions;

namespace Standard;

public class Optional<T>
{
    private T value;

    private bool hasValue;

    public Optional()
    {
        hasValue = false;
    }

    public Optional(T value)
    {
        this.value = value;
        this.hasValue = true;
    }

    public bool HasValue => hasValue;

    public T Value => value;
}
