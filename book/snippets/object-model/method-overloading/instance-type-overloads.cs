using System;

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
        return $"{_prefix} int={value}";
    }

    public string Format(bool value)
    {
        return $"{_prefix} bool={value}";
    }

    public string Format(double value)
    {
        return $"{_prefix} double={value}";
    }

    public string Format(string value)
    {
        return $"{_prefix} string={value}";
    }

    protected Printer()
    {
        this._prefix = "";
    }

    public static Printer __CreateDefault()
    {
        var inst = new Printer();
        return inst;
    }
}
