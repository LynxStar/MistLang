using Standard;

namespace Snippet;

public class Printer
{
    private string _prefix;

    public Printer(string prefix)
    {
        this._prefix = "";
        this._prefix = prefix;
    }

    public string Format(int value)
    {
        return System.FormattableString.Invariant($"{this._prefix} int={value}");
    }

    public string Format(bool value)
    {
        return $"{this._prefix} bool={value}";
    }

    public string Format(double value)
    {
        return System.FormattableString.Invariant($"{this._prefix} double={value}");
    }

    public string Format(string value)
    {
        return $"{this._prefix} string={value}";
    }
}
