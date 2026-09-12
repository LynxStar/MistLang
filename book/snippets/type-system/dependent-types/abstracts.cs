using Standard;

namespace Snippet;

public interface IFixedCollection<TSize>
    where TSize : IConstInt
{
    int GetSize();

    string GetInfo();
}

public class TinyCollection : Snippet.IFixedCollection<Const_Int_8>
{
    public int GetSize()
    {
        return 8;
    }

    public string GetInfo()
    {
        return $"TinyCollection(size={this.GetSize()})";
    }

    public TinyCollection()
    {
    }

    public static TinyCollection __CreateDefault()
    {
        return new TinyCollection();
    }
}
