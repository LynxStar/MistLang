using Standard;

namespace Snippet;

public class Printer
{
    private string _prefix;

    public Printer(string prefix)
    {
        this._prefix = prefix;
    }

    public string Format(int value)
    {
        return System.FormattableString.Invariant($"{_prefix} int={value}");
    }

    public string Format(bool value)
    {
        return $"{_prefix} bool={value}";
    }

    public string Format(double value)
    {
        return System.FormattableString.Invariant($"{_prefix} double={value}");
    }

    public string Format(string value)
    {
        return $"{_prefix} string={value}";
    }
}
