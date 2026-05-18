using System;

namespace Overloading;

public class Printer
{
    private string _prefix;

    public Printer(string prefix)
    {
        this._prefix = prefix;
    }

    public Printer() : this("[default]")
    {
    }

    public static Printer __CreateDefault()
    {
        var inst = new Printer();
        inst._prefix = "";
        return inst;
    }
}
