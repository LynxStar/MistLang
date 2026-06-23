using System;

namespace Snippet;

public class TinyCollection : IFixedCollection<Const_Int_8>
{
    public int GetSize()
    {
        return 8;
    }

    public string GetInfo()
    {
        return $"TinyCollection(size={this.GetSize()})";
    }

    public static TinyCollection __CreateDefault()
    {
        var inst = new TinyCollection();
        return inst;
    }
}
