using System;

namespace Snippet;

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

    private Printer(Mist.Emission.CSharp.__DefaultInit marker)
    {
        this._prefix = "";
    }

    public static Printer __CreateDefault()
    {
        return new Printer(default(Mist.Emission.CSharp.__DefaultInit));
    }
}
