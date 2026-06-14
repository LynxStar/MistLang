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
}
