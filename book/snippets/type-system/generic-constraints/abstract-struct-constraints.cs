using System;

namespace Snippet;

interface IRepository<T>
    where T : class, new()
{
    T GetById(int id);

    void Save(T item);
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
struct Holder<T>
    where T : Snippet.IPrintable
{
    public T Value;

    public Holder(T value)
    {
        Value = value;
    }

    private Holder(T p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Value = p0;
    }

    internal static Holder<T> __FromParts(T p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Holder<T>(p0, default(Mist.Emission.CSharp.__StructInit));
    }
}
